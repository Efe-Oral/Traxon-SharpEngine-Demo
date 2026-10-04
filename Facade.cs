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
    private InstancedMeshNode? _housingsNode;

    private static readonly Color4 HousingColor = new Color4(0.12f, 0.12f, 0.13f, 1); // dark gray
    private static readonly Color4 HoverColor = new Color4(1f, 0.95f, 0.2f, 1); // bright yellow
    private static readonly Color4 SelectedColor = new Color4(1f, 0.95f, 0.2f, 1); // yellow

    // outlines around hovered / selected fixtures. line thickness is in screen pixels, so it stays visible at any zoom
    private MultiLineNode? _hoverOutline;
    private MultiLineNode? _selectionOutline;
    private const float OutlineThickness = 3;

    // how far the outline sits outside the housing (cm). the hover ring is bigger, so on a selected fixture you see both
    private const float SelectionOutlinePadding = 4;
    private const float HoverOutlinePadding = 10;

    // ids of the selected fixtures. a HashSet is like a List without duplicates, and checking "is X in it?" is instant
    private readonly HashSet<int> _selectedFixtureIds = new();
    public int SelectedCount => _selectedFixtureIds.Count;

    // the grid layout, remembered so we can find which fixture is at a point
    private int _columns;
    private int _rows;
    private float _gridWidth;
    private float _gridHeight;

    private int? _hoveredFixtureId;

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

        _columns = columns;
        _rows = rows;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;

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

        // meshes are 1x1, so the scale is the real size
        var housingScale = Matrix4x4.CreateScale(FixtureWidth, FixtureHeight, FixtureDepth);

        foreach (var fixture in _fixtures)
        {
            // scale first, then move
            _housingInstances[fixture.Id] = new WorldColorInstanceData(
                housingScale * Matrix4x4.CreateTranslation(fixture.Position),
                HousingColor
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

        _housingsNode = new InstancedMeshNode(boxMesh, "FixtureHousings");
        _housingsNode.SetInstancesData(_housingInstances);

        // one dot per position, each with its own color. pixels ignore lights, so they look like they glow
        _pixelsNode = new PixelsNode(
            _pixelPositions,
            BoundingBox.FromPoints(_pixelPositions),
            pixelColors: _pixelColors,
            pixelSize: PixelSize,
            hasTransparentPixels: false,
            name: "Pixels"
        );

        // both outlines start hidden, they get their lines when something is hovered or selected
        _selectionOutline = new MultiLineNode("SelectionOutline")
        {
            IsLineStrip = false, // separate lines, each one has its own 2 positions
            LineColor = SelectedColor,
            LineThickness = OutlineThickness,
            Visibility = SceneNodeVisibility.Hidden,
        };
        _hoverOutline = new MultiLineNode("HoverOutline")
        {
            IsLineStrip = false, // separate lines, each one has its own 2 positions
            LineColor = HoverColor,
            LineThickness = OutlineThickness,
            Visibility = SceneNodeVisibility.Hidden,
        };

        RootNode.Add(_housingsNode);
        RootNode.Add(_pixelsNode);
        RootNode.Add(_selectionOutline);
        RootNode.Add(_hoverOutline);
    }

    // which fixture does this ray (e.g. from the mouse) hit? null if none.
    // the facade is a flat grid, so we don't need to test every fixture:
    // find where the ray hits the front of the fixtures, then work out which grid cell that is
    public int? FindFixtureAt(Ray ray)
    {
        // the front faces of all fixtures are on one flat plane, at z = FixtureDepth / 2
        float frontZ = FixtureDepth / 2;
        if (ray.Direction.Z == 0)
            return null; // ray runs parallel to the facade, never hits it

        float distance = (frontZ - ray.Position.Z) / ray.Direction.Z;
        if (distance < 0)
            return null; // the facade is behind the camera

        var hit = ray.Position + ray.Direction * distance;

        // nearest column and row (the reverse of how CreateFixtures placed them)
        int column = (int)MathF.Round((hit.X + _gridWidth / 2) / FixtureSpacing);
        int row = (int)MathF.Round((_gridHeight / 2 - hit.Y) / RowSpacing);

        if (column < 0 || column >= _columns || row < 0 || row >= _rows)
            return null;

        int id = row * _columns + column;
        if (id >= _fixtures.Count)
            return null; // empty spot in the last row

        // the cell is bigger than the fixture, so check we're really on the housing and not in the gap
        var fixture = _fixtures[id];
        if (
            MathF.Abs(hit.X - fixture.Position.X) > FixtureWidth / 2
            || MathF.Abs(hit.Y - fixture.Position.Y) > FixtureHeight / 2
        )
            return null;

        return id;
    }

    // shows the outline around the fixture under the mouse (null = none)
    public void SetHoveredFixture(int? id)
    {
        if (id == _hoveredFixtureId)
            return; // nothing changed, don't rebuild the outline

        _hoveredFixtureId = id;

        var ids = id == null ? Array.Empty<int>() : new[] { id.Value };
        ShowOutline(_hoverOutline, CreateOutlinePositions(ids, HoverOutlinePadding));
    }

    // click: select only this fixture. ctrl + click (addToSelection): add it, or remove it if it was already selected.
    // clicking empty space (id = null) without ctrl clears the selection
    public void ClickFixture(int? id, bool addToSelection)
    {
        if (!addToSelection)
            _selectedFixtureIds.Clear();

        // Add returns false when the id was already in the set, then we remove it instead
        if (id != null && !_selectedFixtureIds.Add(id.Value))
            _selectedFixtureIds.Remove(id.Value);

        UpdateSelectionOutline();
    }

    // box select: selects every fixture whose center lands inside the box on screen.
    // toScreen turns a 3D position into a 2D screen position (the facade doesn't know about the camera, so it gets this from outside).
    // without addToSelection the old selection is replaced
    public void SelectInBox(
        Func<Vector3, Vector2> toScreen,
        Vector2 boxMin,
        Vector2 boxMax,
        bool addToSelection
    )
    {
        if (!addToSelection)
            _selectedFixtureIds.Clear();

        foreach (var fixture in _fixtures)
        {
            // the center of the fixture's front face
            var screen = toScreen(fixture.Position + new Vector3(0, 0, FixtureDepth / 2));

            bool inside =
                screen.X >= boxMin.X
                && screen.X <= boxMax.X
                && screen.Y >= boxMin.Y
                && screen.Y <= boxMax.Y;

            if (inside)
                _selectedFixtureIds.Add(fixture.Id);
        }

        UpdateSelectionOutline();
    }

    // Esc: nothing selected anymore
    public void ClearSelection()
    {
        _selectedFixtureIds.Clear();
        UpdateSelectionOutline();
    }

    private void UpdateSelectionOutline() =>
        ShowOutline(_selectionOutline, CreateOutlinePositions(_selectedFixtureIds, SelectionOutlinePadding));

    // a rectangle (4 lines = 8 positions) around each fixture, a bit bigger than the housing and just in front of it
    private Vector3[] CreateOutlinePositions(IEnumerable<int> ids, float padding)
    {
        var positions = new List<Vector3>();
        float z = FixtureDepth / 2 + 1;

        foreach (int id in ids)
        {
            var center = _fixtures[id].Position;
            float left = center.X - FixtureWidth / 2 - padding;
            float right = center.X + FixtureWidth / 2 + padding;
            float top = center.Y + FixtureHeight / 2 + padding;
            float bottom = center.Y - FixtureHeight / 2 - padding;

            var topLeft = new Vector3(left, top, z);
            var topRight = new Vector3(right, top, z);
            var bottomRight = new Vector3(right, bottom, z);
            var bottomLeft = new Vector3(left, bottom, z);

            // every line needs its own start and end position
            positions.AddRange(new[] { topLeft, topRight, topRight, bottomRight, bottomRight, bottomLeft, bottomLeft, topLeft });
        }

        return positions.ToArray();
    }

    // gives the line node its new positions, or hides it when there is nothing to outline
    private static void ShowOutline(MultiLineNode? node, Vector3[] positions)
    {
        if (node == null)
            return;

        if (positions.Length == 0)
        {
            node.Visibility = SceneNodeVisibility.Hidden;
            return;
        }

        node.Positions = positions; // a new array, so the engine picks it up without UpdatePositions
        node.Visibility = SceneNodeVisibility.Visible;
    }

    // called every frame. asks the effect for the color of every pixel, then dims it by the brightness (0 - 1)
    // selectionColor: when set, the selected fixtures show this color instead of the effect (used by Identify)
    public void UpdateColors(IEffect effect, float seconds, float brightness, Color4? selectionColor = null)
    {
        // same as a normal for loop, but the pixels are split between all cpu cores.
        // safe because every pixel only reads its own position and writes its own color
        Parallel.For(
            0,
            _pixelColors.Length,
            i =>
            {
                var color = effect.GetColor(_pixelFacadePositions[i], seconds);

                _pixelColors[i] = new Color4(
                    color.Red * brightness,
                    color.Green * brightness,
                    color.Blue * brightness,
                    1
                );
            }
        );

        // overwrite the pixels of the selected fixtures, after the effect has run
        if (selectionColor != null)
        {
            foreach (int id in _selectedFixtureIds)
            {
                var fixture = _fixtures[id];
                for (int p = 0; p < fixture.PixelCount; p++)
                    _pixelColors[fixture.FirstPixelIndex + p] = selectionColor.Value;
            }
        }

        // the colors changed, send only the color array to the graphics card again (positions stay there)
        _pixelsNode?.UpdatePixelColors(hasTransparentColors: false);
    }
}
