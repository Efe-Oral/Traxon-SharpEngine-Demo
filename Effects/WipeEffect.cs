using System.Numerics;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// a bright band sweeping from left to right, one sweep every 4 seconds
public class WipeEffect : IEffect
{
    public string Name => "Wipe";
    public string Description => "A warm band sweeping left to right";

    public Color4 GetColor(Vector2 position, float seconds)
    {
        float wipe = seconds / 4 % 1; // goes 0 -> 1, then starts again

        // full brightness at the band, fading out around it
        float distance = Math.Abs(position.X - wipe);
        float brightness = Math.Max(0.05f, 1 - distance * 8);

        return new Color4(brightness, brightness * 0.5f, 0, 1);
    }
}
