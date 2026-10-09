using System;
using NAudio.Wave;

namespace MusicPlayer.DSP;

/// <summary>
/// Real-time sample provider implementing the 6 sound effects using strict mathematical signal-processing formulas.
///
///   1. Electronic Tube - Asymmetric harmonic-distortion tubeshaper (warm analog valve drive)
///   2. 3D Rotate       - Interaural panning + variable delay LFO sweep (360° orbit around the head)
///   3. Surround Sound  - HRTF-style interaural delay + light diffuse echo (headphone surround field)
///   4. Magic Sound     - Vocal-clarity chain: high-pass + mud removal + presence boost + dynamic compression
///   5. Live Treble     - Stadium/live audio: wide reverb + feedback delay + low/high EQ lift
///   6. Tone Low        - Proximity car bass & cabin reverb (Preserved unchanged.)
///
/// Sound effects are strictly isolated from playback speed/pitch rate modifications. Master gain attenuation of 0.85f is applied.
/// </summary>
public class SoundEffectProcessor : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;

    private SoundEffectType _activeEffect = SoundEffectType.UserDefined;
    private bool _isEnabled = false;
    private float _intensity = 1.0f;

    // ---- Live Treble: pure Schroeder smooth reverb (no word repeats) ----
    // 4 parallel comb filters with short, overlapping delays (29/37/41/47ms)
    // diffuse each word into thousands of shards, then 2 all-pass filters
    // (9/5ms) scatter phase. Result: a smooth continuous tail — the voice never
    // audibly repeats like an old echo box.  Wet kept soft (0.25) as a cinema
    // background.  A per-channel chain preserves the stereo image.
    private readonly CombFilter[] _hallCombL;
    private readonly CombFilter[] _hallCombR;
    private readonly AllPassFilter[] _hallAllL;
    private readonly AllPassFilter[] _hallAllR;
    private const float HallWet = 0.25f;   // soft cinematic echo behind the dry

    // ---- Surround Sound: virtual surround via Haas delay + inverted cross-feed ----
    private readonly float[] _surrDelayR;   // right-channel delay buffer (25ms)
    private int _surrDelayLen;
    private int _surrPos;
    private const float SurroundWidth = 0.6f;  // widening strength (0.4-0.8)
    private const float CenterGain = 0.85f;    // keep the center clear

    // ---- Magic Sound: vocal clarity filters + harmonic exciter ----
    private readonly BiquadFilter[] _vocalHp;      // high-pass ~90Hz rumble cleanup
    private readonly BiquadFilter[] _exciterHp;    // high-pass ~4.2kHz feeding the harmonic exciter

    // Magic Sound peaking-EQ EQ coefficients (Direct Form I) per channel
    private readonly float[] _magicX1, _magicX2, _magicY1, _magicY2;
    private float _b0, _b1, _b2, _a1, _a2;
    private const float MagicPresenceDb = 3.5f;   // weighted vocal presence boost (2.5-4kHz zone)
    private const float MagicPresenceFcent = 3300f; // the presence zone centre
    private const float MagicExciterDrive = 1.2f; // subtle treble saturation strength
    private const float MagicExciterMix = 0.30f;  // how much shimmer is returned to the mix

    // Magic Sound sidechain ducking: the words take authority over the music bed.
    private float _duckGain = 1f;                  // current music gain (1.0 = unducked)
    private const float MagicThreshold = 0.05f;    // word-activity sensitivity
    private const float MagicDuckVolume = 0.30f;   // music ducks to 30% while singing
    private const float MagicAttackSpeed = 0.05f;  // duck instantly at the first letter
    private const float MagicReleaseSpeed = 0.005f; // music returns smoothly after silence

    // ---- Tone-shaping Biquad filters (Tone Low: preserved unchanged) ----
    private readonly BiquadFilter[] _toneLowPass;     // Fc=1600Hz (clear vocals & melody through glass)
    private readonly BiquadFilter[] _toneLowShelf;    // Fc=75Hz, +13.5dB sub-bass vibration
    private readonly BiquadFilter[] _trebleHighShelf; // Fc=4200Hz, +9dB

    // ---- Car Cabin Early Reflection Buffers (Tone Low: preserved unchanged) ----
    private readonly float[] _carCabinEchoL = new float[4096];
    private readonly float[] _carCabinEchoR = new float[4096];
    private int _carCabinPosL = 0;
    private int _carCabinPosR = 0;

    private readonly object _lock = new();

    // Fade ramp on effect transitions
    private int _fadeRemaining = 0;
    private int _fadeTotal = 1;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SoundEffectType ActiveEffect
    {
        get => _activeEffect;
        set
        {
            lock (_lock)
            {
                if (_activeEffect == value) return;
                _activeEffect = value;
                _isEnabled = (value != SoundEffectType.UserDefined && (int)value >= 0);
                ResetState();
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public float Intensity
    {
        get => _intensity;
        set => _intensity = Math.Clamp(value, 0f, 1f);
    }

    public SoundEffectProcessor(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;

        _toneLowPass     = new BiquadFilter[_channels];
        _toneLowShelf    = new BiquadFilter[_channels];
        _trebleHighShelf = new BiquadFilter[_channels];

        for (int ch = 0; ch < _channels; ch++)
        {
            _toneLowPass[ch] = new BiquadFilter();
            _toneLowPass[ch].SetLowPass(_sampleRate, 1600f, 0.707f); // Proximity acoustic: Clear vocals through glass at 1600Hz

            _toneLowShelf[ch] = new BiquadFilter();
            _toneLowShelf[ch].SetLowShelf(_sampleRate, 75f, 13.5f, 1.0f); // Massive sub-bass vibration right next to car

            _trebleHighShelf[ch] = new BiquadFilter();
            _trebleHighShelf[ch].SetHighShelf(_sampleRate, 4200f, 9.0f, 1.0f);
        }

        // Schroeder reverb chains: 4 parallel combs + 2 all-passes, per channel
        _hallCombL = new CombFilter[] { new CombFilter(_sampleRate, 29), new CombFilter(_sampleRate, 37), new CombFilter(_sampleRate, 41), new CombFilter(_sampleRate, 47) };
        _hallCombR = new CombFilter[] { new CombFilter(_sampleRate, 29), new CombFilter(_sampleRate, 37), new CombFilter(_sampleRate, 41), new CombFilter(_sampleRate, 47) };
        _hallAllL = new AllPassFilter[] { new AllPassFilter(_sampleRate, 9, 0.7f), new AllPassFilter(_sampleRate, 5, 0.7f) };
        _hallAllR = new AllPassFilter[] { new AllPassFilter(_sampleRate, 9, 0.7f), new AllPassFilter(_sampleRate, 5, 0.7f) };

        _vocalHp     = new BiquadFilter[_channels];
        _exciterHp   = new BiquadFilter[_channels];
        for (int ch = 0; ch < _channels; ch++)
        {
            _vocalHp[ch] = new BiquadFilter();
            _vocalHp[ch].SetHighPass(_sampleRate, 90f, 0.707f);   // light cleanup under 90Hz

            _exciterHp[ch] = new BiquadFilter();
            _exciterHp[ch].SetHighPass(_sampleRate, 4200f, 0.9f); // treble band for the exciter
        }

        // Magic Sound peaking-EQ coefficients (Direct Form I)
        // Presence zone centre 3.3kHz with a one-octave-wide bell (covers 2.5-4kHz),
        // gain set to the weighted 3.5dB.
        float w0 = (float)(2.0 * Math.PI * MagicPresenceFcent / _sampleRate);
        float alpha = (float)(Math.Sin(w0) * Math.Sinh(Math.Log(2.0) / 2.0 * 1.0 * w0 / Math.Sin(w0)));
        float a = (float)Math.Pow(10.0, MagicPresenceDb / 40.0);
        _b0 = 1.0f + alpha * a;
        _b1 = -2.0f * (float)Math.Cos(w0);
        _b2 = 1.0f - alpha * a;
        float a0 = 1.0f + alpha / a;
        _a1 = -2.0f * (float)Math.Cos(w0);
        _a2 = 1.0f - alpha / a;
        _b0 /= a0; _b1 /= a0; _b2 /= a0;
        _a1 /= a0; _a2 /= a0;

        _magicX1 = new float[_channels];
        _magicX2 = new float[_channels];
        _magicY1 = new float[_channels];
        _magicY2 = new float[_channels];

        // Surround: 25ms Haas delay for the right channel
        _surrDelayLen = Math.Max(1, (int)(_sampleRate * 0.025f));
        _surrDelayR = new float[_surrDelayLen];
        _surrPos = 0;

        _lfo3dInc = (2f * MathF.PI * 0.2f) / _sampleRate; // 0.2Hz -> full 360° every 5s
        _lfo3d = 0f;

        _liveLowShelf = new BiquadFilter[_channels];
        if (_channels >= 1)
        {
            _liveLowShelf[0] = new BiquadFilter();
            _liveLowShelf[0].SetLowShelf(_sampleRate, 110f, 6.0f, 1.0f); // PA low body
        }
        if (_channels >= 2)
        {
            _liveLowShelf[1] = new BiquadFilter();
            _liveLowShelf[1].SetLowShelf(_sampleRate, 110f, 6.0f, 1.0f);
        }
    }

    private void ResetState()
    {
        for (int ch = 0; ch < _channels; ch++)
        {
            _toneLowPass[ch].Reset();
            _toneLowShelf[ch].Reset();
            _trebleHighShelf[ch].Reset();
            if (_vocalHp != null)      _vocalHp[ch].Reset();
            if (_exciterHp != null)    _exciterHp[ch].Reset();
            _magicX1[ch] = 0f;
            _magicX2[ch] = 0f;
            _magicY1[ch] = 0f;
            _magicY2[ch] = 0f;
        }

        // Schroeder reverb chains reset
        foreach (var c in _hallCombL) c.Reset();
        foreach (var c in _hallCombR) c.Reset();
        foreach (var a in _hallAllL) a.Reset();
        foreach (var a in _hallAllR) a.Reset();

        // Surround delay reset
        _surrPos = 0;
        Array.Clear(_surrDelayR, 0, _surrDelayLen);

        _lfo3d = 0f;
        _duckGain = 1f;
        if (_liveLowShelf != null)
            foreach (var f in _liveLowShelf) f?.Reset();

        // ---- Tone Low state: preserved unchanged ----
        _carCabinPosL = 0;
        _carCabinPosR = 0;
        Array.Clear(_carCabinEchoL, 0, 4096);
        Array.Clear(_carCabinEchoR, 0, 4096);

        _fadeTotal = Math.Max(1, (int)(_sampleRate * 0.006f));
        _fadeRemaining = _fadeTotal;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read == 0) return 0;

        lock (_lock)
        {
            if (!_isEnabled || _activeEffect == SoundEffectType.UserDefined || (int)_activeEffect < 0)
            {
                return read;
            }

            switch (_activeEffect)
            {
                case SoundEffectType.ElectronicTube:
                    ProcessElectronicTube(buffer, offset, read);
                    break;
                case SoundEffectType.ThreeDRotate:
                    Process3DRotate(buffer, offset, read);
                    break;
                case SoundEffectType.ToneLow:
                    ProcessToneLow(buffer, offset, read);
                    break;
                case SoundEffectType.SurroundSound:
                    ProcessSurroundSound(buffer, offset, read);
                    break;
                case SoundEffectType.MagicSound:
                    ProcessMagicSound(buffer, offset, read);
                    break;
                case SoundEffectType.LiveTreble:
                    ProcessLiveTreble(buffer, offset, read);
                    break;
            }

            // Master Gain Staging: scale final mixed output (ToneLow gets 1.20f volume scaling, others get 0.85f)
            float masterScale = (_activeEffect == SoundEffectType.ToneLow) ? 1.20f : 0.85f;
            for (int i = 0; i < read; i++)
            {
                buffer[offset + i] *= masterScale;
            }

            // Fade in across effect-transition ramp to avoid clicks
            if (_fadeRemaining > 0)
            {
                int frames = read / _channels;
                for (int f = 0; f < frames && _fadeRemaining > 0; f++, _fadeRemaining--)
                {
                    float gain = 1f - (float)_fadeRemaining / _fadeTotal;
                    int baseIdx = offset + f * _channels;
                    if (baseIdx + _channels > offset + read) break;
                    for (int ch = 0; ch < _channels; ch++)
                        buffer[baseIdx + ch] *= gain;
                }
            }
        }

        return read;
    }

    /// <summary>
    /// Electronic Tube: perfectly balanced studio valve warmth (comfortable).
    /// A gentle Tanh saturation adds rich hidden harmonic layers — the "fat"
    /// analog vacuum-tube body — then parallel-processing blends only 35% of
    /// that warmth over 65% of the pure dry signal. The drive is kept soft so
    /// it never approaches harsh digital distortion.
    /// Per the instructional spec: TubeDrive 1.3, BlendLevel 0.35.
    /// </summary>
    private void ProcessElectronicTube(float[] buffer, int offset, int count)
    {
        const float tubeDrive = 1.3f;
        const float blendLevel = 0.35f;

        for (int i = 0; i < count; i++)
        {
            float inputSample = buffer[offset + i];

            // Gentle, smooth tube waveshaping (no harshness)
            float driven = inputSample * tubeDrive;
            float tubeSample = MathF.Tanh(driven);

            // Parallel processing: 35% warm tube over 65% clean dry signal
            float finalSample = tubeSample * blendLevel + inputSample * (1f - blendLevel);

            buffer[offset + i] = Math.Clamp(finalSample, -1f, 1f);
        }
    }

    /// <summary>
    /// 3D Rotate: smooth dynamic stereo panning through a full 360° orbit.
    /// The current angle is advanced continuously; Sin and Cos of that angle
    /// produce the two gain coefficients so the sound glides from L to the
    /// back, to R, to the front and around — without jumps. A CenterWidth
    /// floor keeps a little energy in each ear so the source never vanishes.
    /// Per the instructional spec: RotationSpeedHz 0.2, CenterWidth 0.2.
    /// </summary>
    private void Process3DRotate(float[] buffer, int offset, int count)
    {
        const float CenterWidth = 0.2f;

        for (int i = 0; i + 1 < count && i + 1 < buffer.Length - offset; i += 2)
        {
            // Advance the orbit angle continuously for an endless 360° sweep
            float az = _lfo3d;
            _lfo3d += _lfo3dInc;
            if (_lfo3d >= 2f * MathF.PI) _lfo3d -= 2f * MathF.PI;

            // Circular panning coefficients (0..1) from Cos/Sin of the current angle
            float panLeft  = (MathF.Cos(az) + 1f) * 0.5f;
            float panRight = (MathF.Sin(az) + 1f) * 0.5f;

            // CenterWidth floor prevents a sudden sonic void mid-orbit
            panLeft  = Math.Clamp(panLeft + CenterWidth, 0f, 1f);
            panRight = Math.Clamp(panRight + CenterWidth, 0f, 1f);

            // Apply: L sample (i), R sample (i+1)
            buffer[offset + i]     *= panLeft;
            buffer[offset + i + 1] *= panRight;
        }
    }

    /// <summary>
    /// Tone Low: "Standing Right Next to the Car (Proximity Car Bass & Cabin Reverb)"
    /// Simulates standing directly beside the car window with music & bass blasting inside:
    /// - Clear vocals & melody transmitted through the glass (1600Hz gentle acoustic roll-off).
    /// - Deep, vibrating car chassis sub-bass (+13.5dB at 75Hz).
    /// - Tight car cabin early reflections (14ms, 26ms, 38ms) giving the genuine acoustic car reverberation.
    /// - Hyperbolic Tangent (Tanh) soft saturation preserves kick drum attack and transient punch without clipping.
    /// (PRESERVED UNCHANGED — do not modify.)
    /// </summary>
    private void ProcessToneLow(float[] buffer, int offset, int count)
    {
        int d1 = (int)(_sampleRate * 0.014f); // 14ms window reflection
        int d2 = (int)(_sampleRate * 0.026f); // 26ms windshield reflection
        int d3 = (int)(_sampleRate * 0.038f); // 38ms cabin length reflection

        int posL = _carCabinPosL;
        int posR = _carCabinPosR;
        const int mask = 4095;

        for (int i = 0; i < count; i += 2)
        {
            float rawL = buffer[offset + i];
            float rawR = buffer[offset + i + 1];

            // 1. Through-the-glass acoustic transmission (clear vocals & melody)
            float glassL = _toneLowPass[0].Process(rawL);
            float glassR = _toneLowPass[1].Process(rawR);

            // 2. Heavy sub-bass boost from car subwoofer & vibrating panels
            float bassL = _toneLowShelf[0].Process(glassL);
            float bassR = _toneLowShelf[1].Process(glassR);

            // 3. Car Cabin Reverberation early reflections
            _carCabinEchoL[posL] = bassL;
            _carCabinEchoR[posR] = bassR;

            int tap1L = (posL - d1) & mask;
            int tap2L = (posL - d2) & mask;
            int tap3L = (posL - d3) & mask;

            int tap1R = (posR - d1) & mask;
            int tap2R = (posR - d2) & mask;
            int tap3R = (posR - d3) & mask;

            float cabinEchoL = _carCabinEchoL[tap1L] * 0.35f + _carCabinEchoL[tap2L] * 0.22f + _carCabinEchoL[tap3L] * 0.14f;
            float cabinEchoR = _carCabinEchoR[tap1R] * 0.35f + _carCabinEchoR[tap2R] * 0.22f + _carCabinEchoR[tap3R] * 0.14f;

            posL = (posL + 1) & mask;
            posR = (posR + 1) & mask;

            // Blend direct sound + car cabin reflections
            float mixL = bassL * 0.85f + cabinEchoL * 0.45f;
            float mixR = bassR * 0.85f + cabinEchoR * 0.45f;

            // Tanh soft saturation keeps the kick drum punch crisp without clipping
            buffer[offset + i] = MathF.Tanh(mixL * 0.75f) * 1.25f;
            buffer[offset + i + 1] = MathF.Tanh(mixR * 0.75f) * 1.25f;
        }

        _carCabinPosL = posL;
        _carCabinPosR = posR;
    }

    /// <summary>
    /// Surround Sound: virtual surround for headphones.
    /// Two mechanisms create a wide room-like field beyond the ear cups:
    ///   1. Haas phase delay — the right channel arrives ~25ms after the left,
    ///      fooling the brain into hearing a large 3D environment.
    ///   2. Inverted cross-feed — a small portion of the delayed right channel
    ///      is subtracted from the left and vice-versa, breaking the "inside
    ///      the head" dryness for an expansive surround aura.
    /// Per the instructional spec: SurroundWidth 0.6, CenterGain 0.85.
    /// </summary>
    private void ProcessSurroundSound(float[] buffer, int offset, int count)
    {
        for (int i = 0; i + 1 < count && i + 1 < buffer.Length - offset; i += 2)
        {
            float leftInput = buffer[offset + i];
            float rightInput = buffer[offset + i + 1];

            // 1) Pull the delayed right sample, then store the current right sample
            float delayedRight = _surrDelayR[_surrPos];
            _surrDelayR[_surrPos] = rightInput;

            _surrPos++;
            if (_surrPos >= _surrDelayLen) _surrPos = 0;

            // 2) Inverted cross-feed: cancel the inner-head focus, widen outward
            float surroundLeft = leftInput * CenterGain - delayedRight * SurroundWidth;
            float surroundRight = delayedRight * CenterGain - leftInput * SurroundWidth;

            buffer[offset + i]     = Math.Clamp(surroundLeft, -1f, 1f);
            buffer[offset + i + 1] = Math.Clamp(surroundRight, -1f, 1f);
        }
    }

    /// <summary>
    /// Magic Sound: professional vocal clarity that keeps the singer's pitch intact.
    ///   1. Light high-pass (<90Hz) strips mic-thump and muddy bass rumble.
    ///   2. Harmonic Exciter — an isolated treble band (>4.2kHz) is softly saturated
    ///      (tanh) so it spawns bright companion harmonics that make consonants pop
    ///      (s, sh, t).  The fundamental voice pitch is never shifted — no "squirrel"
    ///      effect.
    ///   3. Weighted Presence Boost — a one-octave peaking bell centred at 3.3kHz,
    ///      boosted 3.5dB, covering the 2.5-4kHz human presence zone where letter
    ///      destinations live.
    ///   4. Sidechain Ducking — the words take authority over the music.  The moment
    ///      the singer speaks, the music bed ducks to 30% instantly (attack), and it
    ///      returns smoothly to full level the instant the voice stops (release).
    /// Per the instructional spec: 3.5dB presence, subtle saturation, duck to 0.30.
    /// </summary>
    private void ProcessMagicSound(float[] buffer, int offset, int count)
    {
        int ch = _channels;
        for (int i = 0; i < count; i++)
        {
            int c = ch > 1 ? (i & 1) : 0; // interleaved channel index

            float raw = buffer[offset + i];

            // 1) Clean the sub-90Hz rumble that buries consonants
            float input = _vocalHp[c].Process(raw);

            // 2) Peaking EQ in the presence zone (Direct Form I bi-quad)
            float output = _b0 * input + _b1 * _magicX1[c] + _b2 * _magicX2[c]
                         - _a1 * _magicY1[c] - _a2 * _magicY2[c];

            _magicX2[c] = _magicX1[c];
            _magicX1[c] = input;
            _magicY2[c] = _magicY1[c];
            _magicY1[c] = output;

            // 3) Harmonic Exciter: isolate the treble band, gently saturate it,
            //    then return a little of the shimmer to the mix.  This adds the
            //    sibilant "shine" without altering the voice pitch.
            float treble = _exciterHp[c].Process(input);
            float excited = MathF.Tanh(treble * MagicExciterDrive);
            float vocalLayer = output + excited * MagicExciterMix;

            // 4) Sidechain Ducking: word activity = how strong the vocals are now.
            float absVocal = MathF.Abs(vocalLayer);
            if (absVocal > MagicThreshold)
            {
                // Singer is talking -> duck the music bed instantly
                _duckGain += (MagicDuckVolume - _duckGain) * MagicAttackSpeed;
            }
            else
            {
                // Singer is silent -> smoothly restore the music
                _duckGain += (1f - _duckGain) * MagicReleaseSpeed;
            }

            // Duck the music bed, then place the crystal words on top (spec: music*duck + vocal)
            float mixed = raw * _duckGain + vocalLayer;

            buffer[offset + i] = Math.Clamp(mixed, -1f, 1f);
        }
    }

    /// <summary>
    /// Professional Schroeder reverb for the empty hall.
    /// Four parallel comb filters with short, overlapping delays (29/37/41/47ms)
    /// diffuse each word into many tiny shards, and two all-pass filters scatter
    /// their phase — so no word ever repeats like an old echo box; the room just
    /// produces a smooth continuous tail.  Wet at 0.25 keeps it as a soft cinema
    /// background behind the dry signal.
    /// </summary>
    private void ProcessLiveTreble(float[] buffer, int offset, int count)
    {
        if (_channels < 2) return;

        for (int i = 0; i + 1 < count && i + 1 < buffer.Length - offset; i += 2)
        {
            float inL = buffer[offset + i];
            float inR = buffer[offset + i + 1];

            // EQ colour of a live PA stage: a touch of high and low lift
            float bodyL = (_trebleHighShelf[0].Process(inL) + _liveLowShelf[0].Process(inL)) * 0.5f;
            float bodyR = (_trebleHighShelf[1].Process(inR) + _liveLowShelf[1].Process(inR)) * 0.5f;

            // 1) Sum the four comb filters (diffuses the word into shards)
            float combL = _hallCombL[0].Process(bodyL) + _hallCombL[1].Process(bodyL)
                        + _hallCombL[2].Process(bodyL) + _hallCombL[3].Process(bodyL);
            float combR = _hallCombR[0].Process(bodyR) + _hallCombR[1].Process(bodyR)
                        + _hallCombR[2].Process(bodyR) + _hallCombR[3].Process(bodyR);
            combL *= 0.25f; // balance the parallel mix
            combR *= 0.25f;

            // 2) Scatter phase through the all-pass filters → smooth continuous tail
            float tailL = _hallAllL[0].Process(_hallAllL[1].Process(combL));
            float tailR = _hallAllR[0].Process(_hallAllR[1].Process(combR));

            // 3) Dry + soft wet reverb background
            float outL = bodyL + tailL * HallWet;
            float outR = bodyR + tailR * HallWet;

            buffer[offset + i]     = 1.4f * MathF.Tanh(outL * (1f / 1.4f));
            buffer[offset + i + 1] = 1.4f * MathF.Tanh(outR * (1f / 1.4f));
        }
    }

    // 3D Rotate LFO phase (per-process, kept here as a field to persist across blocks)
    private float _lfo3d = 0f;
    private float _lfo3dInc;

    // Live Treble low-shelf body (set in ctor)
    private BiquadFilter[] _liveLowShelf;
}
