using System.Numerics;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// an effect answers one question: what color is the pixel at this spot on the facade, at this time?
// position goes from 0 to 1 (x: left -> right, y: bottom -> top)
public interface IEffect
{
    string Name { get; }
    string Description { get; } // one short line, shown under the name in the panel
    Color4 GetColor(Vector2 position, float seconds);
}

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

// every pixel off. on a lighting desk this is called a "blackout"
public class BlackoutEffect : IEffect
{
    public string Name => "Blackout";
    public string Description => "All pixels off";

    public Color4 GetColor(Vector2 position, float seconds) => new Color4(0, 0, 0, 1);
}

// rings of light that start where you click and travel outwards, on a dark facade
public class RippleEffect : IEffect
{
    public string Name => "Ripple";
    public string Description => "Click the facade to send out a ring of light";

    // one ring: where it started (0 - 1 on the facade) and when (in effect seconds)
    private record struct Ripple(Vector2 Center, float StartTime);

    private const float RingSpeed = 0.6f; // how fast a ring grows, in facade heights per second
    private const float RingWidth = 0.08f; // how thick the ring is, in facade heights
    private const float Lifetime = 5; // seconds until a ring has faded out completely
    private const int MaxRipples = 12; // older rings are dropped after this many

    // GetColor runs on many cpu cores at once, while a click adds rings on the UI thread.
    // so we never change the array, we build a new one and swap it in. readers always see a complete array
    private Ripple[] _ripples = Array.Empty<Ripple>();

    // set from outside: the facade's width / height, so rings come out round and not stretched
    public float AspectRatio { get; set; } = 1;

    public void AddRipple(Vector2 center, float startTime)
    {
        var stillVisible = _ripples.Where(r => startTime - r.StartTime < Lifetime).TakeLast(MaxRipples - 1);
        _ripples = stillVisible.Append(new Ripple(center, startTime)).ToArray();
    }

    public Color4 GetColor(Vector2 position, float seconds)
    {
        var ripples = _ripples; // take the current array once
        float brightness = 0;

        foreach (var ripple in ripples)
        {
            float age = seconds - ripple.StartTime;
            if (age < 0 || age > Lifetime)
                continue;

            // distance from the ring's center, measured in facade heights (x is stretched by the aspect ratio)
            float dx = (position.X - ripple.Center.X) * AspectRatio;
            float dy = position.Y - ripple.Center.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // brightest exactly on the ring, fading to 0 at RingWidth away from it
            float radius = age * RingSpeed;
            float onRing = Math.Max(0, 1 - Math.Abs(distance - radius) / RingWidth);

            // the ring fades out as it gets older
            float fade = 1 - age / Lifetime;

            // where two rings cross they add up, but never above full brightness
            brightness = Math.Min(1, brightness + onRing * fade);
        }

        // cool white-blue light
        return new Color4(brightness * 0.55f, brightness * 0.85f, brightness, 1);
    }
}
