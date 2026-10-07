using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// shows a picture on the facade. every light takes the color of the picture at its spot (pixel mapping, like the text effect)
public class ImageEffect : IEffect
{
    public string Name => "Image";
    public string Description => "A picture mapped onto the facade";

    // Fit: the whole picture is visible (black bars if the shapes differ). Fill: covers the whole facade, edges get cropped
    public enum FitMode
    {
        Fit,
        Fill,
    }

    // the picture as loaded from the file (kept so we can shrink it again when the facade or fit mode changes)
    private BitmapSource? _original;

    // the picture shrunk to about the facade's resolution, plus where it sits on the facade.
    // like the text effect: GetColor reads this on many cpu cores, so we only ever swap in a whole new one
    private sealed record Prepared(byte[] Bgra, int Width, int Height, float Left, float Bottom, float ShownWidth, float ShownHeight);
    private Prepared? _prepared;

    private FitMode _fitMode = FitMode.Fit;
    private float _aspectRatio = 1; // facade width / height
    private int _gridWidth = 1; // how many lights across and up the facade has
    private int _gridHeight = 1;

    public string? FileName { get; private set; }

    public FitMode Fit
    {
        get => _fitMode;
        set
        {
            _fitMode = value;
            Prepare();
        }
    }

    // called when the facade is built: its shape and how many lights it has across and up
    public void SetFacade(float aspectRatio, int lightsAcross, int lightsUp)
    {
        _aspectRatio = aspectRatio;
        _gridWidth = Math.Max(1, lightsAcross);
        _gridHeight = Math.Max(1, lightsUp);
        Prepare();
    }

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
        Prepare();
    }

    // works out where the picture sits on the facade, and shrinks it to about the facade's resolution.
    // shrinking first means every light shows the average color of its area, not one random photo pixel (which looks noisy)
    private void Prepare()
    {
        if (_original == null)
            return;

        // all sizes in facade heights: the facade is _aspectRatio wide and 1 tall
        float pictureAspect = (float)_original.PixelWidth / _original.PixelHeight;
        bool pictureIsWider = pictureAspect > _aspectRatio;

        float shownWidth, shownHeight;
        if (_fitMode == FitMode.Fit ? pictureIsWider : !pictureIsWider)
        {
            shownWidth = _aspectRatio; // as wide as the facade
            shownHeight = _aspectRatio / pictureAspect;
        }
        else
        {
            shownHeight = 1; // as tall as the facade
            shownWidth = pictureAspect;
        }

        // centered on the facade
        float left = (_aspectRatio - shownWidth) / 2;
        float bottom = (1 - shownHeight) / 2;

        // how many lights the shown picture covers. shrink to that (never enlarge)
        int width = Math.Clamp((int)Math.Round(_gridWidth * shownWidth / _aspectRatio), 1, _original.PixelWidth);
        int height = Math.Clamp((int)Math.Round(_gridHeight * shownHeight), 1, _original.PixelHeight);

        // draw the picture smaller with smooth (high quality) scaling, then read the pixels back
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
            context.DrawImage(_original, new Rect(0, 0, width, height));

        var small = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        small.Render(visual);

        var bytes = new byte[width * height * 4];
        small.CopyPixels(bytes, width * 4, 0);

        _prepared = new Prepared(bytes, width, height, left, bottom, shownWidth, shownHeight);
    }

    public Color4 GetColor(Vector2 position, float seconds)
    {
        var picture = _prepared; // take the current one once
        if (picture == null)
            return new Color4(0, 0, 0, 1);

        // where this light lands in the picture, from 0 to 1. v goes down, like rows in a picture
        float u = (position.X * _aspectRatio - picture.Left) / picture.ShownWidth;
        float v = 1 - (position.Y - picture.Bottom) / picture.ShownHeight;
        if (u < 0 || u >= 1 || v < 0 || v >= 1)
            return new Color4(0, 0, 0, 1); // outside the picture (black bars with Fit)

        int i = ((int)(v * picture.Height) * picture.Width + (int)(u * picture.Width)) * 4;
        return new Color4(picture.Bgra[i + 2] / 255f, picture.Bgra[i + 1] / 255f, picture.Bgra[i] / 255f, 1);
    }
}
