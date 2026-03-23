using BaseLib.Config;

namespace MintySpire2;

public class Config: SimpleModConfig
{
    [ConfigSection("combat")]
    [SliderRange(0.1, 1.0, 0.1)]
    [SliderLabelFormat("{0:0.0}x")]
    public static double ShuffleSpeed { get; set; } = 0.5;
    
    public static bool ShowIncomingDamage { get; set; } = true;
}