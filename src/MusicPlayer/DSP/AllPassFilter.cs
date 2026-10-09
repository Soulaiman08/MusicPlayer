namespace MusicPlayer.DSP;

/// <summary>
/// All-pass filter for phase diffusion. Passes all frequencies evenly but
/// scatters their phase, which is what turns discrete repeats into a smooth,
/// continuous reverb tail (part of the Schroeder reverb framework).
/// </summary>
public class AllPassFilter
{
    private readonly float[] _buffer;
    private int _bufferIndex = 0;
    private readonly float _feedback;

    public AllPassFilter(int sampleRate, int delayMs, float feedback)
    {
        int bufferSize = Math.Max(1, (int)(sampleRate * (delayMs / 1000.0)));
        _buffer = new float[bufferSize];
        _feedback = feedback;
    }

    public float Process(float input)
    {
        float bufferSample = _buffer[_bufferIndex];
        float output = -input + bufferSample;

        _buffer[_bufferIndex] = input + (bufferSample * _feedback);

        _bufferIndex++;
        if (_bufferIndex >= _buffer.Length) _bufferIndex = 0;

        return output;
    }

    public void Reset()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _bufferIndex = 0;
    }
}