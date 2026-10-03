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

    private const float PixelHeight = 12;

    private readonly List<Fixture> _fixtures = new();

    // one entry per instance: where it is + what color it has
    private WorldColorInstanceData[] _housingInstances = Array.Empty<WorldColorInstanceData>();
    private WorldColorInstanceData[] _pixelInstances = Array.Empty<WorldColorInstanceData>();

    // where each pixel sits on the facade, from 0 to 1. x: 0 = left edge, 1 = right edge. y: 0 = bottom, 1 = top
    private Vector2[] _pixelFacadePositions = Array.Empty<Vector2>();

    public IReadOnlyList<Fixture> Fixtures => _fixtures;
    public int PixelCount => _pixelInstances.Length;

    // width and height of the whole grid
    public Vector2 Size { get; private set; }

    public GroupNode RootNode { get; } = new GroupNode("Facade");
    private InstancedMeshNode? _pixelsNode;

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
        _pixelInstances = new WorldColorInstanceData[totalPixels];
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

            // each pixel gets an equal slot and fills 60% of it to leave gaps between pixels
            float slotWidth = FixtureWidth / fixture.PixelCount;
            var pixelScale = Matrix4x4.CreateScale(slotWidth * 0.6f, PixelHeight, 1);

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

                _pixelInstances[pixelIndex] = new(
                    pixelScale * Matrix4x4.CreateTranslation(pixelPosition),
                    Color4.FromHsv(hue, 1, 1, 1)
                );

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

        // flat quad facing the camera (pixels). This quad is drawn for every entry in the pixel list
        var quadMesh = MeshFactory.CreatePlaneMesh(
            centerPosition: Vector3.Zero,
            planeNormal: new Vector3(0, 0, 1),
            planeHeightDirection: new Vector3(0, 1, 0),
            width: 1,
            height: 1,
            widthSegments: 1,
            heightSegments: 1,
            name: "UnitQuadMesh"
        );

        var housingsNode = new InstancedMeshNode(boxMesh, "FixtureHousings");
        housingsNode.SetInstancesData(_housingInstances);

        // solid color = no shading, so the pixels look like they glow
        _pixelsNode = new InstancedMeshNode(quadMesh, "Pixels") { IsSolidColorMaterial = true };
        _pixelsNode.SetInstancesData(_pixelInstances);

        RootNode.Add(housingsNode);
        RootNode.Add(_pixelsNode);
    }

    // called every frame. asks the effect for the color of every pixel
    public void UpdateColors(IEffect effect, float seconds)
    {
        for (int i = 0; i < _pixelInstances.Length; i++)
        {
            _pixelInstances[i].DiffuseColor = effect.GetColor(_pixelFacadePositions[i], seconds);
        }

        // the array changed, send it to the graphics card again
        _pixelsNode?.UpdateInstancesData(updateBoundingBox: false);
    }
}
