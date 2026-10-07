using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SharpEngine;

// shows a picture file on the facade. the mapping (fit / fill, scrolling, shrinking) is in PictureEffect
public class ImageEffect : PictureEffect
{
    public override string Name => "Image";
    public override string Description => "A picture mapped onto the facade";

    // the picture as loaded from the file (kept so it can be shrunk again when the facade or fit mode changes)
    private BitmapSource? _original;

    public string? FileName { get; private set; }

    protected override int SourceWidth => _original?.PixelWidth ?? 0;
    protected override int SourceHeight => _original?.PixelHeight ?? 0;

    protected override void DrawSource(DrawingContext context, Rect rectangle) => context.DrawImage(_original, rectangle);

    // loads a picture file (jpg, png, bmp ...). has to run on the UI thread, because it uses WPF's image tools
    public void Load(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // read the whole file now, so it isn't kept locked
        bitmap.EndInit();
        bitmap.Freeze();

        _original = bitmap;
        FileName = System.IO.Path.GetFileName(path);
        Refresh();
    }
}
