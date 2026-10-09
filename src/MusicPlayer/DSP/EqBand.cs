namespace MusicPlayer.DSP;

public class EqBand
{
    public float Frequency { get; set; }
    public float Gain { get; set; }
    public float Q { get; set; } = 1.0f;
    
    public EqBand(float frequency, float gain = 0f, float q = 1.0f)
    {
        Frequency = frequency;
        Gain = gain;
        Q = q;
    }
}
