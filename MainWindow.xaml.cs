using System.Numerics;
using System.Windows;
using System.Windows.Input;
using Ab4d.SharpEngine.Cameras;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.Lights;
using Ab4d.SharpEngine.Materials;
using Ab4d.SharpEngine.SceneNodes;
using Ab4d.SharpEngine.Wpf;

namespace SharpEngine;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private TargetPositionCamera? _camera;
    private PointerCameraController? _cameraController;
    private Facade? _facade;
    private bool isCameraRotating = false;

    // E switches to the next one
    private readonly IEffect[] _effects = { new WipeEffect(), new RainbowEffect() };
    private int _currentEffect = 0;

    // live controls. left / right = speed, up / down = brightness
    private float _speed = 1; // 0 = frozen, 1 = normal, 2 = twice as fast
    private float _brightness = 1; // 0 = off, 1 = full

    // where the left button went down, to tell a click apart from a camera drag
    private Point _mouseDownPosition;

    // true while shift + dragging a selection box
    private bool _isBoxSelecting;

    public MainWindow()
    {
        // Ab4d.SharpEngine Trial License can be used for testing the Ab4d.SharpEngine and is valid until November 30, 2026.
        Ab4d.SharpEngine.Licensing.SetLicense(
            licenseOwner: "Efe Oral",
            licenseType: "TrialLicense",
            license: "369C-A35F-8320-CEA6-CA5E-F412-C34D-5EFE-1F40-E62B-A961-F5A3-B007-10E5-8452-EFC7-41E3"
        );
        InitializeComponent();

        CreateScene();
        CreateCamera();
        CreateLight();
        CreateStatsOverlay();
        StartAnimation();
        KeyDown += OnKeyDown;
        MainSceneView.MouseMove += OnMouseMove;
        // "Preview" events reach us before the camera controller sees the mouse, so it can't swallow them
        MainSceneView.PreviewMouseLeftButtonDown += OnLeftMouseDown;
        MainSceneView.PreviewMouseLeftButtonUp += OnLeftMouseUp;
        MainSceneView.MouseLeave += (_, _) => ShowHover(null, new Point());
        Closed += (_, _) => MainSceneView.Dispose();
    }

    private void CreateScene()
    {
        var scene = MainSceneView.Scene;

        // change these two to benchmark, or pass them when running:
        // e.g.: dotnet run -- 500 6 (500 fixtures with 6 pixels each inside)
        int fixtureCount = 1000;
        int pixelsPerFixture = 100;

        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 3)
        {
            fixtureCount = int.Parse(args[1]);
            pixelsPerFixture = int.Parse(args[2]);
        }

        _facade = new Facade(fixtureCount, pixelsPerFixture);
        scene.RootNode.Add(_facade.RootNode);

        // The building wall behind the fixtures (vertical, facing the camera)
        var box = new BoxModelNode(
            centerPosition: new Vector3(0, 0, -30),
            size: new Vector3(_facade.Size.X + 200, _facade.Size.Y + 200, 50),
            material: StandardMaterials.Gray,
            name: "las vegas"
        );
        scene.RootNode.Add(box);
    }

    private void CreateCamera()
    {
        _camera = new TargetPositionCamera()
        {
            TargetPosition = new Vector3(-500, 200, 1000), // center of the fixture row
            Heading = 20, // left/right orbit
            Attitude = -10, // up/down tilt
            Distance = Math.Max(1100, _facade!.Size.X * 1.2f), // far enough to see the whole grid
            ShowCameraLight = ShowCameraLightType.Never, // we use our own point light
        };

        MainSceneView.SceneView.Camera = _camera;

        _cameraController = new PointerCameraController(MainSceneView)
        {
            RotateCameraConditions = PointerAndKeyboardConditions.RightPointerButtonPressed, // right drag rotates, left is for selecting
            MoveCameraConditions = PointerAndKeyboardConditions.MiddlePointerButtonPressed, // hold the wheel and drag to move (pan) the camera
            IsPointerWheelZoomEnabled = true,
        };
    }

    private void CreateLight()
    {
        var scene = MainSceneView.Scene;
        scene.Lights.Clear();
        // These lights only affect the wall and the fixture housings.
        // The pixels are drawn with a solid color and ignore them.
        scene.SetAmbientLight(0.25f);

        var warmLight = new PointLight(position: new Vector3(-175, 0, 0), range: 250f);
        warmLight.Color = new Color3(1f, .6f, .2f);
        scene.RootNode.Add(
            new SphereModelNode(
                warmLight.Position,
                radius: 10f,
                material: new SolidColorMaterial(warmLight.Color),
                name: "warmLightMarker"
            )
        );

        var coldLight = new PointLight(new Vector3(175, 0, 0), 250f);
        coldLight.Color = new Color3(.2f, .4f, 1f);
        scene.RootNode.Add(
            new SphereModelNode(
                centerPosition: coldLight.Position,
                radius: 10f,
                material: new SolidColorMaterial(coldLight.Color),
                name: "coldLightMarker"
            )
        );

        scene.Lights.Add(
            new DirectionalLight(new Vector3(-0.3f, -0.6f, -1f))
            {
                Color = new Color3(0.4f, 0.4f, 0.4f),
            }
        );

        scene.Lights.Add(warmLight);
        scene.Lights.Add(coldLight);
    }

    private void CreateStatsOverlay()
    {
        MainSceneView.SceneView.IsCollectingStatistics = true;

        var timer = System.Diagnostics.Stopwatch.StartNew();
        int frameCount = 0;
        double frameTimeSum = 0;

        // runs after every drawn frame. SceneRendered is an event (kinda like obeserver pattern in Unity)
        MainSceneView.SceneRendered += (_, _) =>
        {
            var stats = MainSceneView.SceneView.Statistics;
            if (stats == null || _facade == null)
                return;

            frameCount++;
            frameTimeSum += stats.UpdateTimeMs + stats.TotalRenderTimeMs;

            // update the text once per second with the averages
            double seconds = timer.Elapsed.TotalSeconds;
            if (seconds < 1)
                return;

            StatsText.Text =
                $"{_facade.Fixtures.Count} fixtures, {_facade.PixelCount} pixels\n"
                + $"avg frame time: {frameTimeSum / frameCount:0.00} ms\n"
                + $"fps: {frameCount / seconds:0}\n"
                + $"effect: {_effects[_currentEffect].Name} (E to change)\n"
                + $"speed: {_speed:0.00}x (left / right)\n"
                + $"brightness: {_brightness * 100:0}% (up / down)\n"
                + $"selected: {_facade.SelectedCount} fixtures";

            frameCount = 0;
            frameTimeSum = 0;
            timer.Restart();
        };
    }

    private void StartAnimation()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double lastTime = 0;
        float effectTime = 0;

        // real DMX fixtures refresh at about 44 Hz, so the colors don't need to change more often than that
        const double colorUpdateInterval = 1.0 / 44;
        double nextColorUpdate = 0;

        // runs before every frame, like Update() in Unity
        MainSceneView.SceneView.SceneUpdating += (_, _) =>
        {
            // the effect has its own clock. each frame it moves forward by the real time that passed, times the speed.
            // so changing the speed doesn't make the effect jump
            double now = clock.Elapsed.TotalSeconds;
            effectTime += (float)(now - lastTime) * _speed;
            lastTime = now;

            // not time for new colors yet, the frame is drawn with the old ones
            if (now < nextColorUpdate)
                return;

            nextColorUpdate += colorUpdateInterval;
            if (nextColorUpdate < now) // we fell behind (slow frames), don't try to catch up
                nextColorUpdate = now + colorUpdateInterval;

            _facade?.UpdateColors(_effects[_currentEffect], effectTime, _brightness);
        };
    }

    // space = start / stop the camera rotation, E = next effect
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _camera != null)
        {
            isCameraRotating = !isCameraRotating;

            if (isCameraRotating)
                _camera.StartRotation(headingChangeInSecond: 20);
            else
                _camera.StopRotation();
        }

        if (e.Key == Key.E)
        {
            // after the last one, go back to the first
            _currentEffect = (_currentEffect + 1) % _effects.Length;
        }

        // Math.Clamp keeps the value inside min and max
        if (e.Key == Key.Right)
            _speed = Math.Clamp(_speed + 0.25f, 0, 4);
        if (e.Key == Key.Left)
            _speed = Math.Clamp(_speed - 0.25f, 0, 4);
        if (e.Key == Key.Up)
            _brightness = Math.Clamp(_brightness + 0.1f, 0, 1);
        if (e.Key == Key.Down)
            _brightness = Math.Clamp(_brightness - 0.1f, 0, 1);
    }

    // mouse moved over the 3D view: find the fixture under it
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_facade == null)
            return;

        var mouse = e.GetPosition(MainSceneView);

        // only show the box once the mouse really moves, so a normal click doesn't flash a tiny box
        if (_isBoxSelecting && (mouse - _mouseDownPosition).Length > 4)
        {
            ShowHover(null, mouse);
            UpdateSelectionBox(mouse);
            return;
        }

        // a ray from the camera through the mouse position, into the scene
        var ray = MainSceneView.SceneView.GetRayFromCamera((float)mouse.X, (float)mouse.Y);
        int? fixtureId = ray.IsValid ? _facade.FindFixtureAt(ray) : null;

        ShowHover(fixtureId, mouse);
    }

    // left button pressed. with shift held it starts a selection box instead of rotating the camera
    private void OnLeftMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPosition = e.GetPosition(MainSceneView);

        // every left press starts a box. if the mouse doesn't move, it turns into a normal click on release
        _isBoxSelecting = true;
        MainSceneView.CaptureMouse(); // keep getting mouse events even if the mouse leaves the view while dragging
    }

    // draws the box from where the button went down to the current mouse position
    private void UpdateSelectionBox(Point mouse)
    {
        SelectionBox.Margin = new Thickness(
            Math.Min(mouse.X, _mouseDownPosition.X),
            Math.Min(mouse.Y, _mouseDownPosition.Y),
            0,
            0
        );
        SelectionBox.Width = Math.Abs(mouse.X - _mouseDownPosition.X);
        SelectionBox.Height = Math.Abs(mouse.Y - _mouseDownPosition.Y);
        SelectionBox.Visibility = Visibility.Visible;
    }

    // the button was released while drawing a box: select what's inside and go back to normal
    private void FinishBoxSelection(Point mouse)
    {
        EndBoxMode();

        var boxMin = new Vector2(
            (float)Math.Min(mouse.X, _mouseDownPosition.X),
            (float)Math.Min(mouse.Y, _mouseDownPosition.Y)
        );
        var boxMax = new Vector2(
            (float)Math.Max(mouse.X, _mouseDownPosition.X),
            (float)Math.Max(mouse.Y, _mouseDownPosition.Y)
        );

        bool shiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // Point3DTo2D turns a 3D position into a position on screen, the same coordinates as the mouse
        _facade?.SelectInBox(
            position => MainSceneView.SceneView.Point3DTo2D(position, adjustByDpiScale: true),
            boxMin,
            boxMax,
            addToSelection: shiftHeld
        );
    }

    // hides the box and stops box mode
    private void EndBoxMode()
    {
        _isBoxSelecting = false;
        SelectionBox.Visibility = Visibility.Collapsed;
        MainSceneView.ReleaseMouseCapture();
    }

    // left button released. if the mouse barely moved since it went down, it was a click (not a camera drag)
    private void OnLeftMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_facade == null)
            return;

        var mouse = e.GetPosition(MainSceneView);

        // moved more than 4 pixels: it was a drag, so select everything in the box
        bool isRealBox = (mouse - _mouseDownPosition).Length > 4;
        if (_isBoxSelecting && isRealBox)
        {
            FinishBoxSelection(mouse);
            return;
        }
        if (_isBoxSelecting)
            EndBoxMode();

        // barely moved: a normal click on one fixture
        var ray = MainSceneView.SceneView.GetRayFromCamera((float)mouse.X, (float)mouse.Y);
        int? fixtureId = ray.IsValid ? _facade.FindFixtureAt(ray) : null;

        // ctrl or shift + click adds / removes, a plain click replaces the selection
        bool addToSelection =
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _facade.ClickFixture(fixtureId, addToSelection);
    }

    // highlights the fixture and shows its label next to the mouse (null hides both)
    private void ShowHover(int? fixtureId, Point mouse)
    {
        _facade?.SetHoveredFixture(fixtureId);

        if (fixtureId == null || _facade == null)
        {
            HoverLabel.Visibility = Visibility.Collapsed;
            return;
        }

        var fixture = _facade.Fixtures[fixtureId.Value];
        int lastPixel = fixture.FirstPixelIndex + fixture.PixelCount - 1;

        HoverText.Text =
            $"Fixture {fixture.Id}\n"
            + $"{fixture.PixelCount} pixels ({fixture.FirstPixelIndex} - {lastPixel})";

        // a bit to the right and below the mouse, so the cursor doesn't cover it
        HoverLabel.Margin = new Thickness(mouse.X + 15, mouse.Y + 15, 0, 0);
        HoverLabel.Visibility = Visibility.Visible;
    }
}
