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

namespace SharpEngine;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        // Ab4d.SharpEngine Trial License can be used for testing the Ab4d.SharpEngine and is valid until November 30, 2026.
        Ab4d.SharpEngine.Licensing.SetLicense(
            licenseOwner: "Efe Oral",
            licenseType: "TrialLicense",
            license: "369C-A35F-8320-CEA6-CA5E-F412-C34D-5EFE-1F40-E62B-A961-F5A3-B007-10E5-8452-EFC7-41E3"
        );
        InitializeComponent();
    }
}
