namespace MusicPlayer.DSP;

public class EqPreset
{
    public string Name { get; set; } = string.Empty;
    public float[] BandGains { get; set; } = new float[10];
    
    public EqPreset() { }
    
    public EqPreset(string name, float[] gains)
    {
        Name = name;
        BandGains = gains;
    }
    
    public static readonly EqPreset[] Presets = new EqPreset[]
    {
        new("Custom", new float[10]),
        new("Normal", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        new("Classical", new float[] { 0, 0, 0, 0, 0, 0, -4, -4, -4, -6 }),
        new("Dance", new float[] { 6, 4, 1, 0, 0, -4, -6, -6, 0, 0 }),
        new("Straightness", new float[] { 3, 2, 0, 0, 0, 0, 0, 1, 2, 3 }),
        new("Folk", new float[] { 0, 0, 0, 3, 4, 4, 3, 0, -2, -3 }),
        new("Heavy Metal", new float[] { 5, 3, 0, -4, -3, 0, 4, 6, 6, 5 }),
        new("Hip Hop", new float[] { 5, 4, 1, 3, -1, -1, 2, -1, 2, 3 }),
        new("Jazz", new float[] { 0, 0, 1, 4, 4, 4, 0, 1, 2, 3 }),
        new("Pop", new float[] { -2, -1, 0, 2, 4, 4, 2, 0, -1, -2 }),
        new("Rock", new float[] { 5, 3, -3, -5, -2, 2, 5, 6, 6, 6 }),
        new("Acoustic", new float[] { 4, 3, 0, 1, 3, 3, 3, 2, 1, 0 }),
        new("Bass Boost", new float[] { 6, 4, 6, 2, 0, 0, 0, 0, 0, 0 }),
        new("Treble Boost", new float[] { 0, 0, 0, 0, 0, 2, 3, 4, 5, 6 }),
        new("Vocal Boost", new float[] { -2, -3, -3, 1, 4, 4, 3, 1, 0, -2 }),
        new("Headphones", new float[] { 2, 2, 0, 0, 2, 3, 4, 5, 0, 0 }),
        new("Deep", new float[] { 0, 0, 2, 6, 6, 6, 2, 0, 0, 0 }),
        new("Electronic", new float[] { 5, 4, 1, 0, -3, 2, 1, 1, 4, 5 }),
        new("Latin", new float[] { 4, 3, 0, 0, -3, -3, -3, 0, 3, 5 }),
        new("Loud", new float[] { 3, 0, 0, -3, -3, 0, 3, 5, 5, 4 }),
        new("Lounge", new float[] { -3, -2, -1, 1, 3, 3, 1, 0, -1, -2 }),
        new("Piano", new float[] { -2, -1, 0, 3, 4, 4, 4, 3, 0, -2 }),
        new("RB", new float[] { 3, 7, 6, 1, -3, -2, 1, 0, 2, 3 }),
    };
}
