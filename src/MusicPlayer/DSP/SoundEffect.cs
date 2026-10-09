namespace MusicPlayer.DSP;

public enum SoundEffectType
{
    ElectronicTube = 0,
    ThreeDRotate = 1,
    ToneLow = 2,
    SurroundSound = 3,
    MagicSound = 4,
    LiveTreble = 5,
    UserDefined = -1
}

public class SoundEffect
{
    public SoundEffectType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    public static readonly SoundEffect[] Effects = new SoundEffect[]
    {
        new()
        {
            Type = SoundEffectType.ElectronicTube,
            Name = "Electronic Tube",
            Description = "Warm analog valve drive"
        },
        new()
        {
            Type = SoundEffectType.ThreeDRotate,
            Name = "3D Rotate",
            Description = "360° dynamic spatial rotation"
        },
        new()
        {
            Type = SoundEffectType.ToneLow,
            Name = "Tone Low",
            Description = "Car bass and acoustic cabin space"
        },
        new()
        {
            Type = SoundEffectType.SurroundSound,
            Name = "Surround Sound",
            Description = "Virtual headphone surround field"
        },
        new()
        {
            Type = SoundEffectType.MagicSound,
            Name = "Magic Sound",
            Description = "Vocal clarity exciter and presence boost"
        },
        new()
        {
            Type = SoundEffectType.LiveTreble,
            Name = "Live Treble",
            Description = "Stadium acoustics and ambient reverb"
        }
    };
}
