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
