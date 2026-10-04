using System.Numerics;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.Meshes;
using Ab4d.SharpEngine.SceneNodes;

namespace SharpEngine;

public class Facade
{
    // sizes in cm
    private const float FixtureWidth = 100;
    private const float FixtureHeight = 20;
    private const float FixtureDepth = 5;
    private const float FixtureSpacing = 150;
    private const float RowSpacing = 60;

    // pixels are drawn as dots with a fixed size on screen (in screen pixels, not cm)
    // 3 leaves small gaps between neighbouring pixels, so moving light reads as separate points
    private const float PixelSize = 3;

    private readonly List<Fixture> _fixtures = new();

    // one entry per housing instance: where it is + what color it has
    private WorldColorInstanceData[] _housingInstances = Array.Empty<WorldColorInstanceData>();

    // pixels keep positions and colors in separate arrays. positions are sent to the graphics card once,
    // colors are sent again whenever they change
    private Vector3[] _pixelPositions = Array.Empty<Vector3>();
    private Color4[] _pixelColors = Array.Empty<Color4>();

    // where each pixel sits on the facade, from 0 to 1. x: 0 = left edge, 1 = right edge. y: 0 = bottom, 1 = top
    private Vector2[] _pixelFacadePositions = Array.Empty<Vector2>();

    public IReadOnlyList<Fixture> Fixtures => _fixtures;
    public int PixelCount => _pixelColors.Length;

    // width and height of the whole grid
    public Vector2 Size { get; private set; }

    public GroupNode RootNode { get; } = new GroupNode("Facade");
    private PixelsNode? _pixelsNode;

    public Facade(int fixtureCount, int pixelsPerFixture)
    {
        CreateFixtures(fixtureCount, pixelsPerFixture);
        CreateInstanceData();
        CreateSceneNodes();
    }

    // a grid, centered on 0,0. filled left to right, top to bottom
    private void CreateFixtures(int fixtureCount, int pixelsPerFixture)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(fixtureCount));
        int rows = (int)Math.Ceiling((double)fixtureCount / columns);

        float gridWidth = (columns - 1) * FixtureSpacing;
        float gridHeight = (rows - 1) * RowSpacing;
        Size = new Vector2(gridWidth + FixtureWidth, gridHeight + FixtureHeight);

        // one fixture = unique id + position + first pixel's index + how many pixel it has
        // e.g.: fixture 0 owns pixels 0 and 1, fixture 1 ownes pixels 2 and 3...
        for (int i = 0; i < fixtureCount; i++)
        {
            int column = i % columns;
            int row = i / columns;

            var position = new Vector3(
                column * FixtureSpacing - gridWidth / 2,
                gridHeight / 2 - row * RowSpacing,
                0
            );
            _fixtures.Add(
                new Fixture(
                    Id: i,
                    Position: position,
                    FirstPixelIndex: i * pixelsPerFixture,
                    PixelCount: pixelsPerFixture
                )
            );
        }
    }

    // creatşon of a single pixel
    private void CreateInstanceData()
    {
        int totalPixels = _fixtures.Sum(f => f.PixelCount);

        _housingInstances = new WorldColorInstanceData[_fixtures.Count];
        _pixelPositions = new Vector3[totalPixels];
        _pixelColors = new Color4[totalPixels];
        _pixelFacadePositions = new Vector2[totalPixels];

        var housingColor = new Color4(0.12f, 0.12f, 0.13f, 1); //gray color for fxture sockets

        // meshes are 1x1, so the scale is the real size
        var housingScale = Matrix4x4.CreateScale(FixtureWidth, FixtureHeight, FixtureDepth);

        foreach (var fixture in _fixtures)
        {
            // scale first, then move
            _housingInstances[fixture.Id] = new WorldColorInstanceData(
                housingScale * Matrix4x4.CreateTranslation(fixture.Position),
                housingColor
            );

            // each pixel sits in the middle of an equal slot along the fixture
            float slotWidth = FixtureWidth / fixture.PixelCount;

            for (int p = 0; p < fixture.PixelCount; p++)
            {
                int pixelIndex = fixture.FirstPixelIndex + p;

                // slightly in front of the housing, otherwise they flicker
                var pixelPosition =
                    fixture.Position
                    + new Vector3(
                        x: -FixtureWidth / 2 + slotWidth * (p + 0.5f),
                        y: 0,
                        z: FixtureDepth / 2 + 0.5f
                    );

                // rainbow, so every pixel is a different color
                float hue = 360f * pixelIndex / totalPixels;

                _pixelPositions[pixelIndex] = pixelPosition;
                _pixelColors[pixelIndex] = Color4.FromHsv(hue, 1, 1, 1);

                // the facade is centered on 0,0, so shift by half the size, then divide by the size
                _pixelFacadePositions[pixelIndex] = new Vector2(
                    (pixelPosition.X + Size.X / 2) / Size.X,
                    (pixelPosition.Y + Size.Y / 2) / Size.Y
                );
            }
        }
    }

    private void CreateSceneNodes()
    {
        // housing boxes
        var boxMesh = MeshFactory.CreateBoxMesh(
            centerPosition: Vector3.Zero,
            size: new Vector3(1, 1, 1),
            name: "UnitBoxMesh"
        );

        var housingsNode = new InstancedMeshNode(boxMesh, "FixtureHousings");
        housingsNode.SetInstancesData(_housingInstances);

        // one dot per position, each with its own color. pixels ignore lights, so they look like they glow
        _pixelsNode = new PixelsNode(
            _pixelPositions,
            BoundingBox.FromPoints(_pixelPositions),
            pixelColors: _pixelColors,
            pixelSize: PixelSize,
            hasTransparentPixels: false,
            name: "Pixels"
        );

        RootNode.Add(housingsNode);
        RootNode.Add(_pixelsNode);
    }

    // how long the last UpdateColors took, split in two parts (for the stats overlay)
    public double LastColorLoopMs { get; private set; }
    public double LastSendMs { get; private set; }

    // called every frame. asks the effect for the color of every pixel, then dims it by the brightness (0 - 1)
    public void UpdateColors(IEffect effect, float seconds, float brightness)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // same as a normal for loop, but the pixels are split between all cpu cores.
        // safe because every pixel only reads its own position and writes its own color
        Parallel.For(0, _pixelColors.Length, i =>
        {
            var color = effect.GetColor(_pixelFacadePositions[i], seconds);

            _pixelColors[i] = new Color4(
                color.Red * brightness,
                color.Green * brightness,
                color.Blue * brightness,
                1
            );
        });

        LastColorLoopMs = timer.Elapsed.TotalMilliseconds; //1st we measure the time it takes to calculate colors of each pixel
        timer.Restart();

        // the colors changed, send only the color array to the graphics card again (positions stay there)
        _pixelsNode?.UpdatePixelColors(hasTransparentColors: false);

        LastSendMs = timer.Elapsed.TotalMilliseconds; //2nd we measure the time it takes to send the color array to GPU
    }
}
