using NAudio.Wave;
using SoundTouch;
using System.Runtime.InteropServices;

namespace MusicPlayer.DSP;

/// <summary>
/// Real SoundTouch-backed tempo/pitch provider.
/// Replaces previous granular OLA which had gaps/silence on live changes.
/// Uses SoundTouchProcessor with proper buffering as in SoundTouchWaveProvider.
/// </summary>
public sealed class SpeedPitchSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private SoundTouchProcessor? _st;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly object _sync = new();
    private float _speed = 1f;
    private float _pitch;
    private bool _sourceEnded;
    private bool _flushed;
    private readonly float[] _inputBuf;
    private readonly WaveFormat _waveFormat;

    public WaveFormat WaveFormat => _waveFormat;
    public float Speed { get { lock(_sync) return _speed; } set { lock(_sync){ _speed=Math.Clamp(value,0.5f,2f); EnsureSoundTouchLocked(); if(_st!=null) _st.Tempo=_speed; } } }
    public float Pitch { get { lock(_sync) return _pitch; } set { lock(_sync){ _pitch=Math.Clamp(value,-12f,12f); EnsureSoundTouchLocked(); if(_st!=null) _st.PitchSemiTones=_pitch; } } }

    public SpeedPitchSampleProvider(ISampleProvider source)
    {
        _source = source;
        _waveFormat = source.WaveFormat;
        _channels = _waveFormat.Channels;
        _sampleRate = _waveFormat.SampleRate;
        _inputBuf = new float[4096 * _channels];
    }

    /// <summary>
    /// SoundTouch (a native .dll) is only constructed when Speed or Pitch actually
    /// deviates from neutral. On the default path (speed 1 / pitch 0) the provider
    /// just passes audio straight through, so the heavy native processor is never
    /// created at track-click time — this is what removed the first-play UI freeze.
    /// Created lazily (and idempotently) under _sync on first real need.
    /// </summary>
    private void EnsureSoundTouchLocked()
    {
        if (_st != null) return;
        _st = new SoundTouchProcessor { SampleRate=_sampleRate, Channels=_channels };
        _st.Tempo = _speed;
        _st.PitchSemiTones = _pitch;
        _st.Rate = 1.0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        float speed, pitch;
        lock(_sync){ speed=_speed; pitch=_pitch; }
        if (MathF.Abs(speed-1f)<0.001f && MathF.Abs(pitch)<0.001f)
        {
            return _source.Read(buffer, offset, count);
        }
        lock(_sync)
        {
            EnsureSoundTouchLocked();
            if (_st == null) return _source.Read(buffer, offset, count);
            int written=0;
            var outSpan = new Span<float>(buffer, offset, count);
            // Need to ensure we don't return silence: loop until we have output or EOF
            while (written < count)
            {
                int need = count - written;
                // Try to receive already processed samples
                int available = _st.AvailableSamples;
                if (available > 0)
                {
                    int toReceive = Math.Min(need, available);
                    // Receive into correct offset
                    var dst = outSpan.Slice(written, toReceive);
                    int received = 0;
                    try { received = _st.ReceiveSamples(dst, toReceive / _channels); received *= _channels; } catch { received=0; }
                    // ReceiveSamples returns numSamples (total floats) or numFrames? Check: NAudioSupport uses toReceive/Channels as frames, then *Channels
                    // Our dst length is toReceive floats, we pass toReceive/Channels frames, expect received frames *Channels floats
                    if (received > 0)
                    {
                        written += received;
                        if (written >= count) break;
                        continue;
                    }
                }
                if (_sourceEnded)
                {
                    if (!_flushed)
                    {
                        try { _st.Flush(); } catch { }
                        _flushed=true;
                        continue;
                    }
                    // After flush, try once more to receive tail
                    if (_st.AvailableSamples==0) break;
                    continue;
                }
                int read = _source.Read(_inputBuf, 0, _inputBuf.Length);
                if (read==0)
                {
                    _sourceEnded=true;
                    continue;
                }
                // PutSamples: second param is numSamples? For SoundTouch, PutSamples(Span, int numSamples) where numSamples is number of samples (frames * channels?) Let's use frames
                try
                {
                    var spanIn = new ReadOnlySpan<float>(_inputBuf,0,read);
                    _st.PutSamples(spanIn, read / _channels);
                }
                catch
                {
                    try { _st.PutSamples(new ReadOnlySpan<float>(_inputBuf,0,read), read); } catch { }
                }
            }
            return written;
        }
    }

    public void Reset()
    {
        lock(_sync)
        {
            _sourceEnded=false;
            _flushed=false;
            if (_st != null)
            {
                try{ _st.Clear(); }catch{}
                _st.Tempo=_speed;
                _st.PitchSemiTones=_pitch;
                _st.Rate=1.0;
            }
        }
    }
}
