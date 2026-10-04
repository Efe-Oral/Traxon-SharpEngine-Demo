using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// effects that use one main color, which the color wheel in the panel can change.
// effects with their own colors (like Rainbow) just don't implement this
public interface IColorEffect
{
    Color4 Color { get; set; }
}
