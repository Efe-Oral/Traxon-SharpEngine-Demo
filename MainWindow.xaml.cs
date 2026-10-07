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
    private BoxModelNode? _wall;

    private bool isCameraRotating = false;

    // all effects, filled in the constructor
    private readonly IEffect[] _effects;
    private readonly RippleEffect _rippleEffect = new(); // kept separately too, because clicks add rings to it
    private readonly TextEffect _textEffect = new("HELLO"); // same, the panel changes its text and mode
    private readonly ImageEffect _imageEffect = new(); // same, the panel picks the picture and fit mode
    private int _currentEffect = 0;

    // the effect's own clock (see StartAnimation). a field so a click knows "now" in effect time
    private float _effectTime;

    // live controls, changed from the panel or the keyboard
    private float _speed = 1; // 0 = frozen, 1 = normal, 2 = twice as fast
    private float _brightness = 1; // 0 = off, 1 = full

    // where the left button went down, to tell a click apart from a camera drag
    private Point _mouseDownPosition;

    // true while shift + dragging a selection box
    private bool _isBoxSelecting;

    // real time since the app started, shared by the animation and Identify
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // identify: when it started (in _clock seconds) and how long it blinks
    private double _identifyStartTime = double.NegativeInfinity; // never started yet
    private const double IdentifyDuration = 2;

    public MainWindow()
    {
        // Ab4d.SharpEngine Trial License can be used for testing the Ab4d.SharpEngine and is valid until November 30, 2026.
        Ab4d.SharpEngine.Licensing.SetLicense(
            licenseOwner: "Efe Oral",
            licenseType: "TrialLicense",
            license: "369C-A35F-8320-CEA6-CA5E-F412-C34D-5EFE-1F40-E62B-A961-F5A3-B007-10E5-8452-EFC7-41E3"
        );
        // the UI is in English, so numbers are written the English way too (1,000 and 1.25) whatever the Windows region is
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        InitializeComponent();

        // every effect in this array shows up in the panel's effect list automatically
        _effects = new IEffect[] { new WipeEffect(), new RainbowEffect(), _rippleEffect, _textEffect, _imageEffect, new BlackoutEffect() };

        CreateScene();
        CreateCamera();
        CreateLight();
        CreateStatsOverlay();
        CreateControlPanel();
        StartAnimation();
        // Preview = the window sees the key before any button or slider does, so the shortcuts always work
        PreviewKeyDown += OnKeyDown;
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

        BuildFacade(fixtureCount, pixelsPerFixture);
    }

    // creates the facade and the wall behind it. used at startup and again when the size changes in the panel
    private void BuildFacade(int fixtureCount, int pixelsPerFixture)
    {
        var scene = MainSceneView.Scene;

        _facade = new Facade(fixtureCount, pixelsPerFixture);
        scene.RootNode.Add(_facade.RootNode);

        // the ripple and text effects need the facade's shape to draw round rings and unstretched letters
        _rippleEffect.AspectRatio = _facade.AspectRatio;
        _textEffect.AspectRatio = _facade.AspectRatio;

        // the image is shrunk to about as many lights as the facade has across and up
        _imageEffect.SetFacade(_facade.AspectRatio, _facade.Columns * pixelsPerFixture, _facade.Rows);

        // The building wall behind the fixtures (vertical, facing the camera)
        _wall = new BoxModelNode(
            centerPosition: new Vector3(0, 0, -30),
            size: new Vector3(_facade.Size.X + 200, _facade.Size.Y + 200, 50),
            material: StandardMaterials.Gray,
            name: "las vegas"
        );
        scene.RootNode.Add(_wall);
    }

    // the Apply button / a preset: throws away the old facade and builds a new one with the new size
    private void RebuildFacade(int fixtureCount, int pixelsPerFixture)
    {
        // building a big facade freezes the window for a moment, so first show a message.
        // then wait 2 screen frames (CompositionTarget.Rendering fires once per frame), so the message is really drawn.
        // (waiting for "when WPF isn't busy" doesn't work here: with a big animated facade it's never not busy)
        BuildingMessage.Visibility = Visibility.Visible;
        int framesWaited = 0;
        EventHandler? afterFrames = null;
        afterFrames = (_, _) =>
        {
            if (++framesWaited < 2)
                return;
            System.Windows.Media.CompositionTarget.Rendering -= afterFrames; // only once

            // forget anything that points at the old fixtures
            ShowHover(null, new Point());
            if (_isBoxSelecting)
                EndBoxMode();

            // remove the old facade and free its memory on the graphics card (hundreds of MB for big facades)
            _facade?.RootNode.DisposeWithAllChildren(disposeMeshes: true, disposeMaterials: true, runSceneCleanup: true);
            if (_wall != null)
            {
                MainSceneView.Scene.RootNode.Remove(_wall);
                _wall.Dispose();
            }

            // the old facade's arrays (positions, colors...) can be hundreds of MB. .NET would free them eventually,
            // asking for it now gives that memory back before we build the new facade
            _facade = null;
            GC.Collect();

            BuildFacade(fixtureCount, pixelsPerFixture);

            // step back far enough to see the whole new facade (same rule as at startup)
            if (_camera != null)
                _camera.Distance = GetCameraDistance();

            // the new facade starts with nothing selected or painted
            UpdateFacadeInfo();
            UpdateSelectionText();
            BuildingMessage.Visibility = Visibility.Collapsed;
        };
        System.Windows.Media.CompositionTarget.Rendering += afterFrames;
    }

    // far enough back to see the whole facade
    private float GetCameraDistance() => Math.Max(1100, _facade!.Size.X * 1.2f);

    private void CreateCamera()
    {
        _camera = new TargetPositionCamera()
        {
            TargetPosition = new Vector3(-500, 200, 1000), // center of the fixture row
            Heading = 20, // left/right orbit
            Attitude = -10, // up/down tilt
            Distance = GetCameraDistance(), // far enough to see the whole grid
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

            // only performance numbers here, the controls live in the panel on the right
            StatsText.Text =
                $"{_facade.Fixtures.Count} fixtures, {_facade.PixelCount} pixels\n"
                + $"avg frame time: {frameTimeSum / frameCount:0.00} ms\n"
                + $"fps: {frameCount / seconds:0}";

            frameCount = 0;
            frameTimeSum = 0;
            timer.Restart();
        };
    }

    // connects the panel's controls to the code. each control has an event, same idea as KeyDown
    private void CreateControlPanel()
    {
        // first, because the effect and selection updates below also update the color section
        CreateColorWheel();
        ClearPaintButton.Click += (_, _) =>
        {
            _facade?.ClearPaint();
            UpdateColorTarget();
        };

        // the list shows every effect (by its Name, see DisplayMemberPath in the XAML)
        EffectList.ItemsSource = _effects;
        EffectList.SelectedIndex = _currentEffect;
        EffectList.SelectionChanged += (_, _) => SetEffect(EffectList.SelectedIndex);

        // e.NewValue is the slider's new position. brightness slider goes 0 - 100, our value 0 - 1
        SpeedSlider.ValueChanged += (_, e) => SetSpeed((float)e.NewValue);
        BrightnessSlider.ValueChanged += (_, e) => SetBrightness((float)e.NewValue / 100);

        IdentifyButton.Click += (_, _) => StartIdentify();

        // text effect settings: every change of the text redraws the text picture, the switch picks the mode
        EffectTextBox.Text = "HELLO";
        EffectTextBox.TextChanged += (_, _) => _textEffect.SetText(EffectTextBox.Text);
        TextStaticButton.Checked += (_, _) => _textEffect.IsScrolling = false;
        TextScrollButton.Checked += (_, _) => _textEffect.IsScrolling = true;

        // image effect settings
        ChooseImageButton.Click += (_, _) => ChooseImage();
        ImageFitButton.Checked += (_, _) => _imageEffect.Fit = ImageEffect.FitMode.Fit;
        ImageFillButton.Checked += (_, _) => _imageEffect.Fit = ImageEffect.FitMode.Fill;
        ImageStaticButton.Checked += (_, _) => _imageEffect.IsScrolling = false;
        ImageScrollButton.Checked += (_, _) => _imageEffect.IsScrolling = true;
        ClearButton.Click += (_, _) => ClearSelection();

        UpdateFacadeInfo();
        CreateFacadeSettings();

        // fill in the value labels once at the start
        SetSpeed(_speed);
        SetBrightness(_brightness);
        UpdateSelectionText();
    }

    // the panel and the keyboard both go through these, so the value and the controls never disagree.
    // setting a slider to the value it already has doesn't fire ValueChanged again, so there's no endless loop
    private void SetEffect(int index)
    {
        if (index < 0)
            return; // the list can briefly have nothing selected
        _currentEffect = index;
        EffectList.SelectedIndex = index;

        // only the Text effect has settings, so its card only shows when it's chosen
        TextSettings.Visibility = _effects[index] == _textEffect ? Visibility.Visible : Visibility.Collapsed;
        ImageSettings.Visibility = _effects[index] == _imageEffect ? Visibility.Visible : Visibility.Collapsed;

        UpdateColorTarget();
    }

    private void SetSpeed(float speed)
    {
        _speed = Math.Clamp(speed, 0, 4); // Math.Clamp keeps the value inside min and max
        SpeedSlider.Value = _speed;
        SpeedValue.Text = $"{_speed:0.00}x";
    }

    private void SetBrightness(float brightness)
    {
        _brightness = Math.Clamp(brightness, 0, 1);
        BrightnessSlider.Value = _brightness * 100;
        BrightnessValue.Text = $"{_brightness * 100:0}%";
    }

    // identify: the selected fixtures blink so you can find them (like "locate" on a real lighting desk)
    private void StartIdentify() => _identifyStartTime = _clock.Elapsed.TotalSeconds;

    private void ClearSelection()
    {
        _facade?.ClearSelection();
        UpdateSelectionText();
    }

    // call after anything that changes the selection
    private void UpdateSelectionText()
    {
        int count = _facade?.SelectedCount ?? 0;
        SelectionText.Text = count switch
        {
            0 => "Nothing selected",
            1 => "1 fixture",
            _ => $"{count:N0} fixtures",
        };

        SelectionDetailText.Text = count == 0
            ? "Click a fixture or drag a box on the facade"
            : $"{_facade!.SelectedPixelCount:N0} pixels";

        // the buttons only make sense with a selection, so they fade out without one
        IdentifyButton.IsEnabled = count > 0;
        ClearButton.IsEnabled = count > 0;

        UpdateColorTarget(); // with a selection the color wheel paints fixtures, without one it colors the effect
    }

    // the normal Windows "open file" window, limited to picture files
    private void ChooseImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an image for the facade",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };

        // ShowDialog returns true when a file was picked (false or null when cancelled)
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _imageEffect.Load(dialog.FileName);
            ImageFileText.Text = _imageEffect.FileName;
        }
        catch (Exception ex)
        {
            // not a picture, or a broken file: tell the user instead of crashing
            ImageFileText.Text = "Couldn't open that file";
            MessageBox.Show(this, ex.Message, "Couldn't open the image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- facade size ----

    // limits for the size boxes, so nobody builds a facade that fills all the memory
    private const int MaxFixtures = 50_000;
    private const int MaxPixelsPerFixture = 200;
    private const long MaxTotalPixels = 10_000_000;

    // the line under the panel title. N0 = number with thousands separators, e.g. 50,000
    private void UpdateFacadeInfo()
    {
        if (_facade != null)
            FacadeInfoText.Text = $"{_facade.Fixtures.Count:N0} fixtures · {_facade.PixelCount:N0} pixels";
    }

    private void CreateFacadeSettings()
    {
        // start with the size we're showing
        if (_facade != null)
        {
            FixtureCountBox.Text = _facade.Fixtures.Count.ToString();
            PixelsPerFixtureBox.Text = (_facade.PixelCount / Math.Max(1, _facade.Fixtures.Count)).ToString();
        }

        // check the numbers every time they change, so the total and the Apply button are always up to date
        FixtureCountBox.TextChanged += (_, _) => ValidateFacadeSize();
        PixelsPerFixtureBox.TextChanged += (_, _) => ValidateFacadeSize();
        ValidateFacadeSize();

        ApplyFacadeButton.Click += (_, _) =>
        {
            if (ReadFacadeSize() is (int fixtures, int pixels))
                RebuildFacade(fixtures, pixels);
        };

        // presets: one click fills in the numbers and builds right away. Tag holds "fixtures,pixels" (see the XAML)
        foreach (var button in new[] { Preset800, Preset100K, Preset1M, Preset2M, Preset5M })
        {
            button.Click += (_, _) =>
            {
                var parts = ((string)button.Tag).Split(',');
                FixtureCountBox.Text = parts[0];
                PixelsPerFixtureBox.Text = parts[1];
                if (ReadFacadeSize() is (int fixtures, int pixels))
                    RebuildFacade(fixtures, pixels);
            };
        }
    }

    // the two numbers if they're valid, otherwise null
    private (int Fixtures, int Pixels)? ReadFacadeSize()
    {
        if (!int.TryParse(FixtureCountBox.Text, out int fixtures) || !int.TryParse(PixelsPerFixtureBox.Text, out int pixels))
            return null;
        if (fixtures < 1 || fixtures > MaxFixtures || pixels < 1 || pixels > MaxPixelsPerFixture)
            return null;
        if ((long)fixtures * pixels > MaxTotalPixels)
            return null;
        return (fixtures, pixels);
    }

    // shows the total, or what's wrong, and only enables Apply for a valid size
    private void ValidateFacadeSize()
    {
        var size = ReadFacadeSize();
        ApplyFacadeButton.IsEnabled = size != null;

        if (size is (int fixtures, int pixels))
            FacadeTotalText.Text = $"= {(long)fixtures * pixels:N0} pixels";
        else
            FacadeTotalText.Text = $"Fixtures 1 - {MaxFixtures:N0}, pixels 1 - {MaxPixelsPerFixture}, max {MaxTotalPixels:N0} in total";
    }

    // ---- color wheel ----

    private const int WheelDisplaySize = 128; // the wheel's size on screen (same as in the XAML)
    private const int WheelBitmapSize = 256; // drawn at twice the size it's shown, so the edge looks smooth

    // draws the wheel once into a bitmap: the angle is the hue (red, yellow, green ...), the distance from the
    // center is the saturation (white in the middle, full color at the edge)
    private void CreateColorWheel()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            WheelBitmapSize, WheelBitmapSize, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        // one int per pixel, holding blue, green, red and alpha bytes
        var pixels = new int[WheelBitmapSize * WheelBitmapSize];
        float radius = WheelBitmapSize / 2f;

        for (int y = 0; y < WheelBitmapSize; y++)
        {
            for (int x = 0; x < WheelBitmapSize; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float distance = MathF.Sqrt(dx * dx + dy * dy) / radius;
                if (distance > 1)
                    continue; // outside the circle: stays transparent

                var color = Color4.FromHsv(AngleToHue(dx, dy), distance, 1, 1);
                pixels[y * WheelBitmapSize + x] =
                    (255 << 24) | ((int)(color.Red * 255) << 16) | ((int)(color.Green * 255) << 8) | (int)(color.Blue * 255);
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, WheelBitmapSize, WheelBitmapSize), pixels, WheelBitmapSize * 4, 0);
        ColorWheelImage.Source = bitmap;

        // press and drag on the wheel to pick. CaptureMouse keeps the drag going even outside the wheel
        ColorWheelImage.MouseLeftButtonDown += (_, e) =>
        {
            ColorWheelImage.CaptureMouse();
            PickColorAt(e.GetPosition(ColorWheelImage));
        };
        ColorWheelImage.MouseMove += (_, e) =>
        {
            if (ColorWheelImage.IsMouseCaptured)
                PickColorAt(e.GetPosition(ColorWheelImage));
        };
        ColorWheelImage.MouseLeftButtonUp += (_, _) => ColorWheelImage.ReleaseMouseCapture();
    }

    // angle around the center in degrees (0 - 360), 0 = pointing right, counting counter-clockwise like a math circle.
    // screen y goes down, so it's flipped
    private static float AngleToHue(float dx, float dy)
    {
        float degrees = MathF.Atan2(-dy, dx) * 180 / MathF.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    // the mouse is at this point on the wheel: work out the color and use it
    private void PickColorAt(Point point)
    {
        float radius = (float)ColorWheelImage.ActualWidth / 2;
        float dx = (float)point.X - radius;
        float dy = (float)point.Y - radius;
        float saturation = Math.Min(1, MathF.Sqrt(dx * dx + dy * dy) / radius); // past the edge counts as the edge

        var color = Color4.FromHsv(AngleToHue(dx, dy), saturation, 1, 1);
        ShowPickedColor(color);

        // with fixtures selected we paint them, otherwise we recolor the effect
        if (_facade != null && _facade.SelectedCount > 0)
        {
            _facade.PaintSelection(color);
            UpdateColorTarget();
        }
        else if (_effects[_currentEffect] is IColorEffect colorEffect)
        {
            colorEffect.Color = color;
        }
    }

    // moves the marker to where this color sits on the wheel, and fills the swatch and hex code
    private void ShowPickedColor(Color4 color)
    {
        var (hue, saturation) = GetHueAndSaturation(color);

        float radius = WheelDisplaySize / 2f;
        double angle = hue * Math.PI / 180;
        System.Windows.Controls.Canvas.SetLeft(ColorWheelMarker, radius + Math.Cos(angle) * saturation * radius - ColorWheelMarker.Width / 2);
        System.Windows.Controls.Canvas.SetTop(ColorWheelMarker, radius - Math.Sin(angle) * saturation * radius - ColorWheelMarker.Height / 2);

        byte r = (byte)(color.Red * 255), g = (byte)(color.Green * 255), b = (byte)(color.Blue * 255);
        ColorSwatch.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        ColorHexText.Text = $"#{r:X2}{g:X2}{b:X2}";
    }

    // the opposite of FromHsv, for the hue and saturation part (the wheel has no dark colors, so value is ignored)
    private static (float Hue, float Saturation) GetHueAndSaturation(Color4 color)
    {
        float max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        float min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        float delta = max - min;
        if (delta == 0)
            return (0, 0); // a gray or white: sits in the center

        float hue;
        if (max == color.Red)
            hue = 60 * ((color.Green - color.Blue) / delta % 6);
        else if (max == color.Green)
            hue = 60 * ((color.Blue - color.Red) / delta + 2);
        else
            hue = 60 * ((color.Red - color.Green) / delta + 4);

        return (hue < 0 ? hue + 360 : hue, delta / max);
    }

    // the line above the wheel that says what it colors, plus the effect's current color on the wheel
    private void UpdateColorTarget()
    {
        int selected = _facade?.SelectedCount ?? 0;
        var effect = _effects[_currentEffect];

        if (selected > 0)
        {
            ColorTargetText.Text = selected == 1 ? "Painting the selected fixture" : $"Painting the {selected:N0} selected fixtures";
        }
        else if (effect is IColorEffect colorEffect)
        {
            ColorTargetText.Text = $"Coloring the {effect.Name} effect. Select fixtures to paint them instead";
            ShowPickedColor(colorEffect.Color);
        }
        else
        {
            ColorTargetText.Text = $"{effect.Name} has its own colors. Select fixtures to paint them";
        }

        ClearPaintButton.IsEnabled = (_facade?.PaintedCount ?? 0) > 0;
    }

    private void StartAnimation()
    {
        double lastTime = 0;

        // real DMX fixtures refresh at about 44 Hz, so the colors don't need to change more often than that
        const double colorUpdateInterval = 1.0 / 44;
        double nextColorUpdate = 0;

        // runs before every frame, like Update() in Unity
        MainSceneView.SceneView.SceneUpdating += (_, _) =>
        {
            // the effect has its own clock. each frame it moves forward by the real time that passed, times the speed.
            // so changing the speed doesn't make the effect jump
            double now = _clock.Elapsed.TotalSeconds;
            _effectTime += (float)(now - lastTime) * _speed;
            lastTime = now;

            // not time for new colors yet, the frame is drawn with the old ones
            if (now < nextColorUpdate)
                return;

            nextColorUpdate += colorUpdateInterval;
            if (nextColorUpdate < now) // we fell behind (slow frames), don't try to catch up
                nextColorUpdate = now + colorUpdateInterval;

            _facade?.UpdateColors(_effects[_currentEffect], _effectTime, _brightness, GetIdentifyColor(now));
        };
    }

    // while identifying, the selected fixtures blink white / off. returns null when not identifying
    private Color4? GetIdentifyColor(double now)
    {
        double sinceStart = now - _identifyStartTime;
        if (sinceStart < 0 || sinceStart > IdentifyDuration)
            return null;

        // 4 blinks per second: on for the first half of each blink, off for the second half
        bool on = sinceStart * 4 % 1 < 0.5;
        return on ? new Color4(1, 1, 1, 1) : new Color4(0, 0, 0, 1);
    }

    // keyboard shortcuts. they do the same as the panel controls
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // while typing in the text box, letters are text and not shortcuts. Enter or Esc finishes typing
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                // Enter in one of the facade size boxes also builds the new facade
                bool inSizeBox = Keyboard.FocusedElement == FixtureCountBox || Keyboard.FocusedElement == PixelsPerFixtureBox;
                if (e.Key == Key.Enter && inSizeBox && ReadFacadeSize() is (int fixtures, int pixels))
                    RebuildFacade(fixtures, pixels);

                ReleaseTextFocus();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                ToggleCameraRotation();
                break;
            case Key.E:
                SetEffect((_currentEffect + 1) % _effects.Length); // after the last one, back to the first
                break;
            case Key.Right:
                SetSpeed(_speed + 0.25f);
                break;
            case Key.Left:
                SetSpeed(_speed - 0.25f);
                break;
            case Key.Up:
                SetBrightness(_brightness + 0.1f);
                break;
            case Key.Down:
                SetBrightness(_brightness - 0.1f);
                break;
            case Key.I:
                StartIdentify();
                break;
            case Key.Escape:
                ClearSelection();
                break;
            default:
                return; // not one of ours, let WPF handle it normally
        }

        // we used the key, so the focused button / slider / list doesn't also react to it
        e.Handled = true;
    }

    // takes keyboard focus away from the text box and back to the window, so the shortcuts work again
    private void ReleaseTextFocus()
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            Focus();
    }

    private void ToggleCameraRotation()
    {
        if (_camera == null)
            return;

        isCameraRotating = !isCameraRotating;

        if (isCameraRotating)
            _camera.StartRotation(headingChangeInSecond: 20);
        else
            _camera.StopRotation();
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
        ReleaseTextFocus(); // clicking the facade means you're done typing

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

        // ctrl + drag adds to the selection, a plain drag replaces it (same key as ctrl + click)
        bool ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        // Point3DTo2D turns a 3D position into a position on screen, the same coordinates as the mouse
        _facade?.SelectInBox(
            position => MainSceneView.SceneView.Point3DTo2D(position, adjustByDpiScale: true),
            boxMin,
            boxMax,
            addToSelection: ctrlHeld
        );
        UpdateSelectionText();
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

        // barely moved: a click
        var ray = MainSceneView.SceneView.GetRayFromCamera((float)mouse.X, (float)mouse.Y);

        // with the Ripple effect on, a click starts a ring of light there instead of selecting (dragging still box selects)
        if (_effects[_currentEffect] == _rippleEffect)
        {
            if (ray.IsValid && _facade.GetFacadePosition(ray) is Vector2 center)
                _rippleEffect.AddRipple(center, _effectTime);
            return;
        }

        // otherwise: a normal click on one fixture
        int? fixtureId = ray.IsValid ? _facade.FindFixtureAt(ray) : null;

        // ctrl + click adds / removes, a plain click replaces the selection
        bool ctrlHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _facade.ClickFixture(fixtureId, addToSelection: ctrlHeld);
        UpdateSelectionText();
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
