namespace MusicPlayer.DSP;

/// <summary>
/// Comb filter (delayed feedback with damping). Used in parallel inside the
/// Schroeder reverb: each comb spreads the signal across a slightly different
/// short delay, so no single delay dominates and no mechanical word-repeat is
/// audible. Feedback extends the tail; Dampening dulls harsh high frequencies.
/// </summary>
public class CombFilter
{
    private readonly float[] _buffer;
    private int _bufferIndex = 0;
    private const float Feedback = 0.74f;   // reverb-tail length
    private const float Dampening = 0.25f;  // removes metallic ringing
    private float _filterStore = 0f;

    public CombFilter(int sampleRate, int delayMs)
    {
        int bufferSize = Math.Max(1, (int)(sampleRate * (delayMs / 1000.0)));
        _buffer = new float[bufferSize];
    }

    public float Process(float input)
    {
        float output = _buffer[_bufferIndex];

        // Dampen the tail before feeding back (kills harsh treble buildup)
        _filterStore = output + Dampening * (_filterStore - output);
        _buffer[_bufferIndex] = input + (_filterStore * Feedback);

        _bufferIndex++;
        if (_bufferIndex >= _buffer.Length) _bufferIndex = 0;

        return output;
    }

    public void Reset()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _bufferIndex = 0;
        _filterStore = 0f;
    }
}