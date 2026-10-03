using System.Numerics;

namespace SharpEngine;

// One light fixture. Its pixels live in one big shared array,
// so it only remembers where they start and how many it has.
public readonly record struct Fixture(
    int Id,
    Vector3 Position,
    int FirstPixelIndex,
    int PixelCount
);

// for example for 10 fixtures with 4 pixels, fixture 6 would be:
// Fixture(Id: 6, Position: (75,0,0), FirstPixelIndex: 24, PixelCount: 4)
// so for fixture 6, FirstPixelIndex: 24. Hence, it owns pixels 24, 25, 26, 27
