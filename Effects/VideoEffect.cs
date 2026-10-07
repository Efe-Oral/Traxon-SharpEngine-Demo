using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace SharpEngine;

// plays a video file on the facade. Windows' built-in video player (WPF MediaPlayer) plays it in the background,
// and we "photograph" its current frame up to 30 times a second. each frame is then shown like an image (see PictureEffect)
public class VideoEffect : PictureEffect
{
    public override string Name => "Video";
    public override string Description => "A video file played on the facade";

    // grabbing a frame costs a few milliseconds (more for big videos), and videos have about 30 frames a second anyway
    private const double MinSecondsBetweenFrames = 1.0 / 30;

    private readonly MediaPlayer _player = new() { IsMuted = true }; // a facade has no speakers
    private readonly Stopwatch _sinceLastFrame = Stopwatch.StartNew();
    private bool _isOpen;
    private bool _isActive; // is the Video effect the chosen one right now
    private double _speed = 1;

    public string? FileName { get; private set; }

    // play / pause from the panel. the video only really plays while the Video effect is chosen
    public bool IsPlaying { get; private set; }

    // the speed slider: 1 = normal, 2 = twice as fast, 0 = stopped
    public double Speed
    {
        get => _speed;
        set
        {
            _speed = value;
            UpdatePlayer();
        }
    }

    // set when the user picks another effect, so a hidden video doesn't keep playing and using the CPU
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            UpdatePlayer();
        }
    }

    public VideoEffect()
    {
        // once the file is open we know the video's size, so the first frame can be shown
        _player.MediaOpened += (_, _) =>
        {
            _isOpen = true;
            Refresh();
        };

        // loop: when the video ends, start it again from the beginning
        _player.MediaEnded += (_, _) =>
        {
            _player.Position = TimeSpan.Zero;
            UpdatePlayer();
        };
    }

    protected override int SourceWidth => _isOpen ? _player.NaturalVideoWidth : 0;
    protected override int SourceHeight => _isOpen ? _player.NaturalVideoHeight : 0;

    protected override void DrawSource(DrawingContext context, Rect rectangle) => context.DrawVideo(_player, rectangle);

    // opens a video file and starts playing it. the file is read in the background, MediaOpened tells us when it's ready.
    // if the file can't be played, MediaFailed fires instead (the window shows a message)
    public void Load(string path)
    {
        _isOpen = false;
        _player.Open(new Uri(path));
        FileName = System.IO.Path.GetFileName(path);
        IsPlaying = true;
        UpdatePlayer();
    }

    public event EventHandler<ExceptionEventArgs>? Failed
    {
        add => _player.MediaFailed += value;
        remove => _player.MediaFailed -= value;
    }

    public void TogglePlay()
    {
        IsPlaying = !IsPlaying;
        UpdatePlayer();
    }

    // called on every color update while the Video effect is chosen: grab the current frame (at most 30 times a second)
    public void UpdateFrame()
    {
        if (!_isOpen || _sinceLastFrame.Elapsed.TotalSeconds < MinSecondsBetweenFrames)
            return;

        _sinceLastFrame.Restart();
        Refresh();
    }

    // plays only when the user wants it to, the effect is chosen and the speed isn't 0
    private void UpdatePlayer()
    {
        if (IsPlaying && _isActive && _speed > 0)
        {
            _player.SpeedRatio = _speed;
            _player.Play();
        }
        else
        {
            _player.Pause();
        }
    }

    public void Close() => _player.Close();
}
