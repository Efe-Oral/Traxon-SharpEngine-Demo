using System.Numerics;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

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
