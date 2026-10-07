using System.Numerics;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.Core;
using Ab4d.SharpEngine.Materials;
using Ab4d.SharpEngine.Meshes;
using Ab4d.SharpEngine.SceneNodes;
using Ab4d.SharpEngine.Vulkan;
using Ab4d.Vulkan;

namespace SharpEngine;

// TEMP prototype of Andrej Benedik's suggestion: the pixel colors go into a picture (texture) instead of millions of dots.
// two ways to show that picture:
//   OnePlane:             Andrej's idea as he wrote it. one big rectangle over the whole facade, every picture pixel is one light
//   RectanglePerFixture:  one small rectangle on the front of each fixture, each showing its own strip of the picture
// both build a new picture on every color update. only the layout of the picture and the shape it's drawn on differ
public enum TextureMode
{
    OnePlane,
    RectanglePerFixture,
}

public class TexturePrototype
{
    // biggest picture width we use. graphics cards support at least this much
    private const int MaxTextureWidth = 16384;

    // rectangle per fixture: a dark gap after every light, like the old quads had. doubles the picture size.
    // off: from a distance the gaps made the lights look dim and grainy (tested)
    private const bool GapsBetweenPixels = false;

    // rectangle per fixture: how tall the strip of lights is (cm), same as the old quad pixels
    private const float StripHeight = 12;

    private readonly Facade _facade;
    private readonly int _width; // picture size in picture pixels
    private readonly int _height;

    // where each light's color goes in the picture (index of its first byte). worked out once
    private readonly int[] _byteIndexOfPixel;

    // the picture, 4 bytes per picture pixel in blue, green, red, alpha order
    private readonly byte[] _imageBytes;

    private readonly SolidColorMaterial _material = new("FacadeTextureMaterial");
    private GpuImage? _texture;

    public SceneNode Node { get; }

    public TexturePrototype(Facade facade, TextureMode mode)
    {
        _facade = facade;
        _byteIndexOfPixel = new int[facade.PixelCount];

        // every fixture has the same number of pixels
        int pixelsPerFixture = facade.Fixtures[0].PixelCount;

        if (mode == TextureMode.OnePlane)
        {
            // laid out like the facade: (columns x pixels per fixture) wide, one row per fixture row
            _width = facade.Columns * pixelsPerFixture;
            _height = facade.Rows;

            // the same column / row numbering as Facade.CreateFixtures: left to right, then top to bottom
            foreach (var fixture in facade.Fixtures)
            {
                int column = fixture.Id % facade.Columns;
                int row = fixture.Id / facade.Columns;
                for (int p = 0; p < fixture.PixelCount; p++)
                    _byteIndexOfPixel[fixture.FirstPixelIndex + p] = (row * _width + column * pixelsPerFixture + p) * 4;
            }

            // the plane covers every column and row cell of the grid, just in front of the fixtures (behind the selection outlines)
            Node = new PlaneModelNode(
                centerPosition: new Vector3(0, 0, facade.FrontZ + 0.5f),
                size: new Vector2(facade.Columns * Facade.ColumnSpacing, facade.Rows * Facade.RowSpacingCm),
                normal: new Vector3(0, 0, 1),
                heightDirection: new Vector3(0, 1, 0),
                name: "TexturePlane"
            )
            {
                Material = _material,
            };
        }
        else
        {
            // packed like a sticker sheet: every fixture gets a short horizontal strip, as many strips per row as fit
            int texelsPerPixel = GapsBetweenPixels ? 2 : 1; // light (+ dark gap)
            int stripWidth = pixelsPerFixture * texelsPerPixel;
            int stripsPerRow = MaxTextureWidth / stripWidth;

            _width = Math.Min(facade.Fixtures.Count, stripsPerRow) * stripWidth;
            _height = (facade.Fixtures.Count + stripsPerRow - 1) / stripsPerRow; // rounded up

            // one mesh with 4 corners (2 triangles) per fixture. each rectangle sits on its fixture
            // and its texture coordinates (0 - 1 across the picture) point at that fixture's strip
            var vertices = new PositionNormalTextureVertex[facade.Fixtures.Count * 4];
            var triangleIndices = new int[facade.Fixtures.Count * 6];
            var normal = new Vector3(0, 0, 1);
            float halfWidth = Facade.FixtureWidthCm / 2;
            float halfHeight = StripHeight / 2;
            float z = facade.FrontZ + 0.5f;

            foreach (var fixture in facade.Fixtures)
            {
                int stripX = fixture.Id % stripsPerRow * stripWidth;
                int stripY = fixture.Id / stripsPerRow;

                for (int p = 0; p < fixture.PixelCount; p++)
                    _byteIndexOfPixel[fixture.FirstPixelIndex + p] = (stripY * _width + stripX + p * texelsPerPixel) * 4;

                // u: from the strip's left edge to its right edge. a tiny inset so the edge never picks the neighbour's strip.
                // v: the middle of the strip's row, so the whole rectangle shows exactly that one row
                float u0 = (stripX + 0.01f) / _width;
                float u1 = (stripX + stripWidth - 0.01f) / _width;
                float v = (stripY + 0.5f) / _height;

                var center = fixture.Position;
                int i = fixture.Id * 4;
                vertices[i] = new PositionNormalTextureVertex(center + new Vector3(-halfWidth, halfHeight, z), normal, new Vector2(u0, v));
                vertices[i + 1] = new PositionNormalTextureVertex(center + new Vector3(halfWidth, halfHeight, z), normal, new Vector2(u1, v));
                vertices[i + 2] = new PositionNormalTextureVertex(center + new Vector3(halfWidth, -halfHeight, z), normal, new Vector2(u1, v));
                vertices[i + 3] = new PositionNormalTextureVertex(center + new Vector3(-halfWidth, -halfHeight, z), normal, new Vector2(u0, v));

                // two triangles: top-left, top-right, bottom-right and top-left, bottom-right, bottom-left
                int t = fixture.Id * 6;
                triangleIndices[t] = i;
                triangleIndices[t + 1] = i + 1;
                triangleIndices[t + 2] = i + 2;
                triangleIndices[t + 3] = i;
                triangleIndices[t + 4] = i + 2;
                triangleIndices[t + 5] = i + 3;
            }

            var mesh = new StandardMesh(vertices, triangleIndices, name: "FixtureRectangles");

            // the same material on both sides, so it shows no matter which way the triangles turned out to face
            Node = new MeshModelNode(mesh, _material, "FixtureRectangles") { BackMaterial = _material };
        }

        // start all black and fully opaque (alpha 255). gaps and unused spots stay black
        _imageBytes = new byte[_width * _height * 4];
        for (int i = 3; i < _imageBytes.Length; i += 4)
            _imageBytes[i] = 255;

        // no blurring between neighbouring picture pixels, so every light stays a crisp block
        _material.DiffuseTextureSamplerType = CommonSamplerTypes.ClampNoInterpolation;
    }

    // turns the facade's current pixel colors into a new picture and shows it
    public void Update(VulkanDevice gpuDevice)
    {
        var colors = _facade.PixelColors;

        // Color4 (0 - 1 floats) to bytes (0 - 255), on all cpu cores
        Parallel.For(0, colors.Length, i =>
        {
            int b = _byteIndexOfPixel[i];
            var c = colors[i];
            _imageBytes[b] = (byte)(c.Blue * 255);
            _imageBytes[b + 1] = (byte)(c.Green * 255);
            _imageBytes[b + 2] = (byte)(c.Red * 255);
        });

        // like Andrej explained: don't overwrite the old picture (the GPU may still be drawing it), make a new one.
        // Dispose only frees the old one once the GPU is done with it
        _texture?.Dispose();

        var rawImage = new RawImageData(_width, _height, _width * 4, Format.B8G8R8A8Unorm, _imageBytes, checkTransparency: false);
        _texture = new GpuImage(gpuDevice, rawImage, generateMipMaps: false, imageSource: "FacadeTexture");
        _material.DiffuseTexture = _texture;
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        Node.Dispose();
    }
}
