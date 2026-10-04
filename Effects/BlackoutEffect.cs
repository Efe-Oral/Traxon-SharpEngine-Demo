using System.Numerics;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// every pixel off. on a lighting desk this is called a "blackout"
public class BlackoutEffect : IEffect
{
    public string Name => "Blackout";
    public string Description => "All pixels off";

    public Color4 GetColor(Vector2 position, float seconds) => new Color4(0, 0, 0, 1);
}
