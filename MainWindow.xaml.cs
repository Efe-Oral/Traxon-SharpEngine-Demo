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
        Closed += (_, _) => MainSceneView.Dispose();
    }

    private void CreateScene()
    {
        var scene = MainSceneView.Scene;

        var plane = new PlaneModelNode(
            centerPosition: new Vector3(0, 0, 0),
            size: new Vector2(400, 400),
            normal: new Vector3(0, 1, 0), // facing up
            heightDirection: new Vector3(0, 0, -1),
            name: "Floor"
        )
        {
            Material = StandardMaterials.Gray,
            BackMaterial = StandardMaterials.Black,
        };
        scene.RootNode.Add(plane);

        float radius = 30;
        var sphere = new SphereModelNode("Ball")
        {
            CenterPosition = new Vector3(0, radius, 0), // sits on the floor
            Radius = radius,
            Material = StandardMaterials.Orange,
        };
        scene.RootNode.Add(sphere);
    }

    private void CreateCamera()
    {
        _camera = new TargetPositionCamera()
        {
            TargetPosition = new Vector3(0, 20, 0), // look near the sphere
            Heading = -40, // left/right orbit
            Attitude = -25, // up/down tilt
            Distance = 400, // how far away
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

        // Soft fill so the dark side of the sphere is not pure black
        scene.SetAmbientLight(0.25f);

        // A lamp above and in front of the sphere
        scene.Lights.Add(new PointLight(position: new Vector3(80, 120, 100)));
    }
}
