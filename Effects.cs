using System.Numerics;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// an effect answers one question: what color is the pixel at this spot on the facade, at this time?
// position goes from 0 to 1 (x: left -> right, y: bottom -> top)
public interface IEffect
{
    string Name { get; }
    Color4 GetColor(Vector2 position, float seconds);
}

// a bright band sweeping from left to right, one sweep every 4 seconds
public class WipeEffect : IEffect
{
    public string Name => "Wipe";

    public Color4 GetColor(Vector2 position, float seconds)
    {
        float wipe = seconds / 4 % 1; // goes 0 -> 1, then starts again

        // full brightness at the band, fading out around it
        float distance = Math.Abs(position.X - wipe);
        float brightness = Math.Max(0.05f, 1 - distance * 8);

        return new Color4(brightness, brightness * 0.5f, 0, 1);
    }
}

// the whole color wheel spread across the facade, sliding to the left
public class RainbowEffect : IEffect
{
    public string Name => "Rainbow";

    public Color4 GetColor(Vector2 position, float seconds)
    {
        float hue = (position.X + seconds * 0.2f) % 1 * 360; // 0 - 360
        return Color4.FromHsv(hue, 1, 1, 1);
    }
}
