using System;
using NAudio.Wave;
using MusicPlayer.DSP;

namespace MusicPlayer.Audio;

public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;

    public static readonly float[] CenterFrequencies = new float[10]
    {
        31.25f, 62.5f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
    };

    private readonly BiquadFilter[][] _eqFilters; // [channel][band]
    private readonly BiquadFilter[] _bassBoostFilters; // [channel]

    private float[] _bandGains = new float[10];
    private bool _eqEnabled = true;
    private bool _bassBoostEnabled = false;
    private float _bassBoostStrength = 0f;
    private bool _virtualizerEnabled = false;
    private float _virtualizerStrength = 0f;
    private float _balance = 0f;

    private readonly object _lock = new();

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsEnabled
    {
        get => _eqEnabled;
        set => _eqEnabled = value;
    }

    public float Balance
    {
        get => _balance;
        set => _balance = Math.Clamp(value, -1f, 1f);
    }

    public bool BassBoostEnabled
    {
        get => _bassBoostEnabled;
        set
        {
            _bassBoostEnabled = value;
            UpdateBassBoost();
        }
    }

    public float BassBoostStrength
    {
        get => _bassBoostStrength;
        set
        {
            _bassBoostStrength = Math.Clamp(value, 0f, 1f);
            UpdateBassBoost();
        }
    }

    public bool VirtualizerEnabled
    {
        get => _virtualizerEnabled;
        set => _virtualizerEnabled = value;
    }

    public float VirtualizerStrength
    {
        get => _virtualizerStrength;
        set => _virtualizerStrength = Math.Clamp(value, 0f, 1f);
    }

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;

        _eqFilters = new BiquadFilter[_channels][];
        _bassBoostFilters = new BiquadFilter[_channels];

        for (int ch = 0; ch < _channels; ch++)
        {
            _eqFilters[ch] = new BiquadFilter[10];
            for (int b = 0; b < 10; b++)
            {
                _eqFilters[ch][b] = new BiquadFilter();
                ConfigureFilter(ch, b, 0f);
            }

            _bassBoostFilters[ch] = new BiquadFilter();
            _bassBoostFilters[ch].SetLowShelf(_sampleRate, 80f, 0f, 0.5f);
        }
    }

    private void ConfigureFilter(int channel, int band, float gainDb)
    {
        float targetFreq = CenterFrequencies[band];
        float nyquist = _sampleRate * 0.5f;

        // If band is at or above Nyquist, keep filter flat to prevent mathematical breakdown
        if (targetFreq >= nyquist * 0.96f)
        {
            _eqFilters[channel][band].SetFlat();
        }
        else
        {
            float safeFreq = MathF.Min(targetFreq, nyquist * 0.92f);
            _eqFilters[channel][band].SetPeaking(_sampleRate, safeFreq, gainDb, 1.4f);
        }
    }

    private bool _hasActiveEqBands = false;

    private void UpdateActiveEqFlag()
    {
        bool active = false;
        for (int b = 0; b < 10; b++)
        {
            if (MathF.Abs(_bandGains[b]) > 0.05f) { active = true; break; }
        }
        _hasActiveEqBands = active;
    }

    public void SetBandGain(int band, float gainDb)
    {
        if (band < 0 || band >= 10) return;
        gainDb = Math.Clamp(gainDb, -15f, 15f);

        lock (_lock)
        {
            _bandGains[band] = gainDb;
            for (int ch = 0; ch < _channels; ch++)
            {
                ConfigureFilter(ch, band, gainDb);
            }
            UpdateActiveEqFlag();
        }
    }

    public void SetAllBands(float[] gains)
    {
        if (gains == null || gains.Length < 10) return;
        lock (_lock)
        {
            for (int b = 0; b < 10; b++)
            {
                float gainDb = Math.Clamp(gains[b], -15f, 15f);
                _bandGains[b] = gainDb;
                for (int ch = 0; ch < _channels; ch++)
                {
                    ConfigureFilter(ch, b, gainDb);
                }
            }
            UpdateActiveEqFlag();
        }
    }

    public float GetBandGain(int band)
    {
        if (band < 0 || band >= 10) return 0f;
        return _bandGains[band];
    }

    private void UpdateBassBoost()
    {
        lock (_lock)
        {
            float boostDb = _bassBoostEnabled ? (_bassBoostStrength * 12f) : 0f;
            for (int ch = 0; ch < _channels; ch++)
            {
                _bassBoostFilters[ch].SetLowShelf(_sampleRate, 80f, boostDb, 0.5f);
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read == 0) return 0;

        bool eqActive;
        bool bassActive;
        bool virtActive;
        float balance;
        float virtStrength;

        lock (_lock)
        {
            eqActive = _eqEnabled && _hasActiveEqBands;
            bassActive = _bassBoostEnabled && _bassBoostStrength > 0.01f;
            virtActive = _virtualizerEnabled && _channels == 2 && _virtualizerStrength > 0.01f;
            balance = _balance;
            virtStrength = _virtualizerStrength;
        }

        // Fast zero-CPU bypass when all controls are in neutral/flat state
        if (!eqActive && !bassActive && !virtActive && balance == 0f)
        {
            return read;
        }

        // 1. Process 10-Band EQ & Bass Boost
        if (eqActive || bassActive)
        {
            for (int i = 0; i < read; i += _channels)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    int sampleIdx = offset + i + ch;
                    if (sampleIdx < offset + read)
                    {
                        float sample = buffer[sampleIdx];
                        if (eqActive)
                        {
                            for (int b = 0; b < 10; b++)
                            {
                                sample = _eqFilters[ch][b].Process(sample);
                            }
                        }
                        if (bassActive)
                        {
                            sample = _bassBoostFilters[ch].Process(sample);
                        }
                        buffer[sampleIdx] = Math.Clamp(sample, -1.0f, 1.0f);
                    }
                }
            }
        }

        // 2. Virtualizer (Stereo spatial widening)
        if (virtActive)
        {
            float width = 1.0f + virtStrength * 0.8f;
            int stereoFrames = read / 2;
            for (int f = 0; f < stereoFrames; f++)
            {
                int leftIdx = offset + f * 2;
                if (leftIdx + 1 >= offset + read) break;
                float left = buffer[leftIdx];
                float right = buffer[leftIdx + 1];

                float mid = (left + right) * 0.5f;
                float side = (left - right) * 0.5f * width;

                buffer[leftIdx] = Math.Clamp(mid + side, -1.0f, 1.0f);
                buffer[leftIdx + 1] = Math.Clamp(mid - side, -1.0f, 1.0f);
            }
        }

        // 3. Stereo Balance
        if (balance != 0f && _channels >= 2)
        {
            float leftFactor = (balance < 0f) ? 1.0f : (1.0f - balance);
            float rightFactor = (balance > 0f) ? 1.0f : (1.0f + balance);

            int stereoFrames = read / 2;
            for (int f = 0; f < stereoFrames; f++)
            {
                int leftIdx = offset + f * 2;
                if (leftIdx + 1 >= offset + read) break;
                buffer[leftIdx] *= leftFactor;
                buffer[leftIdx + 1] *= rightFactor;
            }
        }

        return read;
    }

    public void Reset()
    {
        lock (_lock)
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                for (int b = 0; b < 10; b++)
                {
                    _eqFilters[ch][b].Reset();
                }
                _bassBoostFilters[ch].Reset();
            }
        }
    }
}
