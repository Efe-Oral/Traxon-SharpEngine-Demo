using System.Numerics;

namespace SharpEngine;

// One light fixture. Its pixels live in one big shared array,
// so it only remembers where they start and how many it has.
public readonly record struct Fixture(int Id, Vector3 Position, int FirstPixelIndex, int PixelCount);
