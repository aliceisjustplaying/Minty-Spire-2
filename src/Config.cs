using BaseLib.Config;

namespace MintySpire2;

public class Config: SimpleModConfig
{
    [ConfigSection("combat")]
    [ConfigSlider(0.1, 1.0, 0.1, Format = "{0:0.0}x")]
    public static double ShuffleSpeed { get; set; } = 0.5;
    
    public static bool ShowIncomingDamage { get; set; } = true;
    
    [ConfigSection("misc")]
    public static bool ChangeRewardOrder { get; set; } = true;

    public static bool ForceAeonglassAct3Boss { get; set; } = false;

    public static AeonglassBossPlacement AeonglassBossPlacement { get; set; } = AeonglassBossPlacement.Random;
}

public enum AeonglassBossPlacement
{
    First,
    Second,
    Random
}
