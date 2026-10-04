using System.Numerics;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// the whole color wheel spread across the facade, sliding to the left
public class RainbowEffect : IEffect
{
    public string Name => "Rainbow";
    public string Description => "The color wheel sliding across the facade";

    public Color4 GetColor(Vector2 position, float seconds)
    {
        float hue = (position.X + seconds * 0.2f) % 1 * 360; // 0 - 360
        return Color4.FromHsv(hue, 1, 1, 1);
    }
}
