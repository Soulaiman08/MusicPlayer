using NAudio.Wave;

namespace MusicPlayer.DSP;

public class LoudnessSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiquadFilter[] _lowFilters;
    private readonly BiquadFilter[] _highFilters;
    private readonly int _channels;
    private readonly int _sampleRate;
    private bool _enabled;
    private float _gainDb;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool IsEnabled { get => _enabled; set => _enabled = value; }
    public float GainDb { get => _gainDb; set { _gainDb = Math.Clamp(value, -12f, 12f); UpdateFilters(); } }

    public LoudnessSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        _lowFilters = new BiquadFilter[_channels];
        _highFilters = new BiquadFilter[_channels];
        for(int c=0;c<_channels;c++) { _lowFilters[c]=new BiquadFilter(); _highFilters[c]=new BiquadFilter(); }
        UpdateFilters();
    }
    private void UpdateFilters()
    {
        float g = _enabled ? _gainDb : 0;
        for(int c=0;c<_channels;c++)
        {
            _lowFilters[c].SetLowShelf(_sampleRate, 120f, g*0.6f, 0.7f);
            _highFilters[c].SetHighShelf(_sampleRate, 10000f, g*0.6f, 0.7f);
        }
    }
    public int Read(float[] buffer, int offset, int count)
    {
        int read=_source.Read(buffer,offset,count);
        if(!_enabled || Math.Abs(_gainDb)<0.01f) return read;
        for(int i=0;i<read;i+=_channels)
            for(int ch=0;ch<_channels;ch++)
                if(offset+i+ch < buffer.Length)
                {
                    float s = _lowFilters[ch].Process(buffer[offset+i+ch]);
                    s = _highFilters[ch].Process(s);
                    buffer[offset+i+ch]=s;
                }
        return read;
    }
}
