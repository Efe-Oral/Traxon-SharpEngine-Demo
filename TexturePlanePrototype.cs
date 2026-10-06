using System.Numerics;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.Core;
using Ab4d.SharpEngine.Materials;
using Ab4d.SharpEngine.SceneNodes;
using Ab4d.SharpEngine.Vulkan;
using Ab4d.Vulkan;

namespace SharpEngine;

// TEMP prototype of Andrej Benedik's suggestion: instead of millions of dots, draw ONE flat plane over the facade
// with an image (texture) on it, where every image pixel is one light. every color update builds a new image.
public class TexturePlanePrototype
{
    private readonly Facade _facade;
    private readonly int _width; // image size in pixels: one image pixel per light
    private readonly int _height;

    // where each light's color goes in the image (index of its first byte). worked out once
    private readonly int[] _byteIndexOfPixel;

    // the image, 4 bytes per pixel in blue, green, red, alpha order
    private readonly byte[] _imageBytes;

    private readonly SolidColorMaterial _material = new("FacadeTextureMaterial");
    private GpuImage? _texture;

    public PlaneModelNode Node { get; }

    public TexturePlanePrototype(Facade facade)
    {
        _facade = facade;

        // every fixture has the same number of pixels, so the image is (columns x pixels per fixture) wide and one row per fixture row
        int pixelsPerFixture = facade.Fixtures[0].PixelCount;
        _width = facade.Columns * pixelsPerFixture;
        _height = facade.Rows;

        // start all black and fully opaque (alpha 255). spots without a light (end of the last row) stay black
        _imageBytes = new byte[_width * _height * 4];
        for (int i = 3; i < _imageBytes.Length; i += 4)
            _imageBytes[i] = 255;

        // the same column / row numbering as Facade.CreateFixtures: left to right, then top to bottom
        _byteIndexOfPixel = new int[facade.PixelCount];
        foreach (var fixture in facade.Fixtures)
        {
            int column = fixture.Id % facade.Columns;
            int row = fixture.Id / facade.Columns;

            for (int p = 0; p < fixture.PixelCount; p++)
            {
                int x = column * pixelsPerFixture + p;
                _byteIndexOfPixel[fixture.FirstPixelIndex + p] = (row * _width + x) * 4;
            }
        }

        // no blurring between neighbouring image pixels, so every light stays a crisp block
        _material.DiffuseTextureSamplerType = CommonSamplerTypes.ClampNoInterpolation;

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

    // turns the facade's current pixel colors into a new image and puts it on the plane
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

        // like Andrej explained: don't overwrite the old image (the GPU may still be drawing it), make a new one.
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
