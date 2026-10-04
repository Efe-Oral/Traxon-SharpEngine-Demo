using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ab4d.SharpEngine.Common;

namespace SharpEngine;

// shows text on the facade, standing still or scrolling from right to left.
// the text is drawn once into a small black and white picture (a "mask"), and every pixel looks up its spot in that picture.
// that's pixel mapping, the same idea a video would use
public class TextEffect : IEffect, IColorEffect
{
    public string Name => "Text";
    public string Description => "Your own text, standing still or scrolling";

    public Color4 Color { get; set; } = new Color4(1, 0.95f, 0.85f, 1); // warm white

    private const float TextHeight = 0.7f; // part of the facade height the text fills
    private const float ScrollSpeed = 0.5f; // in facade heights per second

    // the picture of the text: how much each spot is covered by a letter (0 - 1), row by row
    private sealed record TextMask(float[] Coverage, int Width, int Height);

    // like the ripples: GetColor reads this on many cpu cores, so we only ever swap in a whole new mask
    private TextMask? _mask;

    public bool IsScrolling { get; set; }

    // set from outside: the facade's width / height, so the letters don't get stretched
    public float AspectRatio { get; set; } = 1;

    public TextEffect(string text) => SetText(text);

    // draws the text into a new mask. has to run on the UI thread, because it uses WPF's text drawing
    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _mask = null;
            return;
        }

        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var formattedText = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 48, Brushes.White, 1.0);

        int width = (int)Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace) + 2;
        int height = (int)Math.Ceiling(formattedText.Height);

        // draw the text into an off-screen bitmap
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawText(formattedText, new Point(1, 0));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        // read the pixels back. 4 bytes per pixel (blue, green, red, alpha), the alpha byte tells how covered it is
        var bytes = new byte[width * height * 4];
        bitmap.CopyPixels(bytes, width * 4, 0);

        var coverage = new float[width * height];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = bytes[i * 4 + 3] / 255f;

        _mask = new TextMask(coverage, width, height);
    }

    public Color4 GetColor(Vector2 position, float seconds)
    {
        var mask = _mask; // take the current mask once
        if (mask == null)
            return new Color4(0, 0, 0, 1);

        // work in facade heights, so x and y use the same units (x goes from 0 to AspectRatio)
        float x = position.X * AspectRatio;
        float y = position.Y;

        // the text box: TextHeight tall, centered vertically. its width follows from the picture's shape
        float textWidth = TextHeight * mask.Width / mask.Height;
        float top = 0.5f + TextHeight / 2;

        float left;
        if (IsScrolling)
        {
            // starts just past the right edge and moves left until it's fully gone, then repeats
            float travel = AspectRatio + textWidth;
            left = AspectRatio - seconds * ScrollSpeed % travel;
        }
        else
        {
            left = (AspectRatio - textWidth) / 2; // centered
        }

        // where this pixel lands in the picture, from 0 to 1 (v goes down, like rows in a picture)
        float u = (x - left) / textWidth;
        float v = (top - y) / TextHeight;
        if (u < 0 || u >= 1 || v < 0 || v >= 1)
            return new Color4(0, 0, 0, 1);

        float covered = mask.Coverage[(int)(v * mask.Height) * mask.Width + (int)(u * mask.Width)];

        return new Color4(Color.Red * covered, Color.Green * covered, Color.Blue * covered, 1);
    }
}
