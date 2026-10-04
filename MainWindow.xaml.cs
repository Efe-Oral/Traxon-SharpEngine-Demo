using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Ab4d.SharpEngine.Cameras;
using Ab4d.SharpEngine.Common;
using Ab4d.SharpEngine.Lights;
using Ab4d.SharpEngine.Materials;
using Ab4d.SharpEngine.SceneNodes;
using Ab4d.SharpEngine.Utilities;
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
    private bool isCameraRotating = true;

    // E switches to the next one
    private readonly IEffect[] _effects = { new WipeEffect(), new RainbowEffect() };
    private int _currentEffect = 0;

    // live controls. left / right = speed, up / down = brightness
    private float _speed = 1; // 0 = frozen, 1 = normal, 2 = twice as fast
    private float _brightness = 1; // 0 = off, 1 = full

    // how long our own color update takes. the engine's frame time doesn't include it
    private double _updateTimeSum;
    private double _loopTimeSum; // part 1: working out the colors
    private double _sendTimeSum; // part 2: sending the array to the graphics card
    private int _updateCount;

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
        Closed += (_, _) => MainSceneView.Dispose();
    }

    private void CreateScene()
    {
        var scene = MainSceneView.Scene;

        // change these two to benchmark, or pass them when running:
        // e.g.: dotnet run -- 500 6 (500 fixtures with 6 pixels each inside)
        int fixtureCount = 500;
        int pixelsPerFixture = 70;

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
            TargetPosition = new Vector3(0, 0, 0), // center of the fixture row
            Heading = 20, // left/right orbit
            Attitude = -10, // up/down tilt
            Distance = Math.Max(1100, _facade!.Size.X * 1.2f), // far enough to see the whole grid
            ShowCameraLight = ShowCameraLightType.Never, // we use our own point light
        };

        MainSceneView.SceneView.Camera = _camera;

        _cameraController = new PointerCameraController(MainSceneView)
        {
            RotateCameraConditions = PointerAndKeyboardConditions.LeftPointerButtonPressed,
            MoveCameraConditions =
                PointerAndKeyboardConditions.LeftPointerButtonPressed
                | PointerAndKeyboardConditions.ControlKey,
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
                + $"avg color update: {_updateTimeSum / Math.Max(1, _updateCount):0.00} ms\n"
                + $"  colors loop: {_loopTimeSum / Math.Max(1, _updateCount):0.00} ms\n"
                + $"  send to GPU: {_sendTimeSum / Math.Max(1, _updateCount):0.00} ms\n"
                + $"effect: {_effects[_currentEffect].Name} (E to change)\n"
                + $"speed: {_speed:0.00}x (left / right)\n"
                + $"brightness: {_brightness * 100:0}% (up / down)";

            frameCount = 0;
            frameTimeSum = 0;
            _updateTimeSum = 0;
            _loopTimeSum = 0;
            _sendTimeSum = 0;
            _updateCount = 0;
            timer.Restart();
        };

        // keep the camera turning so the engine keeps drawing frames
        _camera?.StartRotation(headingChangeInSecond: 20);
    }

    private void StartAnimation()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double lastTime = 0;
        float effectTime = 0;

        // runs before every frame, like Update() in Unity
        MainSceneView.SceneView.SceneUpdating += (_, _) =>
        {
            // the effect has its own clock. each frame it moves forward by the real time that passed, times the speed.
            // so changing the speed doesn't make the effect jump
            double now = clock.Elapsed.TotalSeconds;
            effectTime += (float)(now - lastTime) * _speed;
            lastTime = now;

            var updateTimer = System.Diagnostics.Stopwatch.StartNew();
            _facade?.UpdateColors(_effects[_currentEffect], effectTime, _brightness);
            _updateTimeSum += updateTimer.Elapsed.TotalMilliseconds;
            _loopTimeSum += _facade?.LastColorLoopMs ?? 0;
            _sendTimeSum += _facade?.LastSendMs ?? 0;
            _updateCount++;
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
}
