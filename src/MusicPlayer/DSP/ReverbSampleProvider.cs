using NAudio.Wave;

namespace MusicPlayer.DSP;

public enum ReverbPreset { None, SmallRoom, MediumRoom, LargeRoom, MediumHall, LargeHall, Plate }

public class ReverbSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;
    private ReverbPreset _preset = ReverbPreset.None;
    private bool _enabled;
    private float[] _delayBuffer = Array.Empty<float>();
    private int _delayPos;
    private int _delaySamples;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public ReverbPreset Preset { get => _preset; set { _preset = value; UpdateDelay(); } }
    public bool IsEnabled { get => _enabled; set => _enabled = value; }

    public ReverbSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        UpdateDelay();
    }

    private void UpdateDelay()
    {
        int ms = _preset switch
        {
            ReverbPreset.None => 0,
            ReverbPreset.SmallRoom => 35,
            ReverbPreset.MediumRoom => 75,
            ReverbPreset.LargeRoom => 120,
            ReverbPreset.MediumHall => 180,
            ReverbPreset.LargeHall => 260,
            ReverbPreset.Plate => 90,
            _ => 0
        };
        _delaySamples = ms == 0 ? 0 : (int)(_sampleRate * ms / 1000.0) * _channels;
        _delayBuffer = _delaySamples > 0 ? new float[_delaySamples] : Array.Empty<float>();
        _delayPos = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!_enabled || _preset == ReverbPreset.None || read == 0) return read;
        float decay = _preset switch { ReverbPreset.SmallRoom=>0.25f, ReverbPreset.MediumRoom=>0.35f, ReverbPreset.LargeRoom=>0.45f, ReverbPreset.MediumHall=>0.5f, ReverbPreset.LargeHall=>0.6f, ReverbPreset.Plate=>0.4f, _=>0.3f };
        int delayLen = _delayBuffer.Length;
        if (delayLen == 0) return read;
        int pos = _delayPos;
        for (int i = 0; i < read; i++)
        {
            int idx = offset + i;
            float dry = buffer[idx];
            float delayed = _delayBuffer[pos];
            _delayBuffer[pos] = dry + delayed * decay * 0.5f;
            if (++pos >= delayLen) pos = 0;
            buffer[idx] = dry * 0.85f + delayed * 0.35f;
        }
        _delayPos = pos;
        return read;
    }
}
