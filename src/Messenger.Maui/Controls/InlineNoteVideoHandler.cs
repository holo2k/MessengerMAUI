using Microsoft.Maui.Handlers;
#if ANDROID
using NativeVideoView = Android.Views.TextureView;
#elif IOS
using NativeVideoView = UIKit.UIView;
#endif

namespace Messenger.Maui.Controls;

public sealed class InlineNoteVideoHandler : ViewHandler<InlineNoteVideo, NativeVideoView>
{
    public static readonly IPropertyMapper<InlineNoteVideo, InlineNoteVideoHandler> NoteMapper = new PropertyMapper<InlineNoteVideo, InlineNoteVideoHandler>(ViewMapper)
    {
        [nameof(InlineNoteVideo.Source)] = (handler, _) => handler.UpdateSource(),
        [nameof(InlineNoteVideo.IsPlaying)] = (handler, _) => handler.UpdatePlayback()
    };
    public InlineNoteVideoHandler() : base(NoteMapper) { }
#if ANDROID
    private Android.Media.MediaPlayer? _player;
    private Android.Views.Surface? _surface;
    private SurfaceListener? _listener;
    private bool _prepared;
    public double PositionMs => _prepared ? _player?.CurrentPosition ?? 0 : 0;
    public double DurationMs => _prepared ? _player?.Duration ?? 0 : 0;
    protected override NativeVideoView CreatePlatformView() => new(Context);
    protected override void ConnectHandler(NativeVideoView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.SurfaceTextureListener = _listener = new SurfaceListener(this);
    }
    private void UpdateSource()
    {
        ReleasePlayer();
        if (_surface is null || VirtualView.Source is not { } path) return;
        try
        {
            var player = _player = new Android.Media.MediaPlayer();
            player.SetSurface(_surface);
            player.SetAudioAttributes(new Android.Media.AudioAttributes.Builder()!.SetUsage(Android.Media.AudioUsageKind.Media)!
                .SetContentType(Android.Media.AudioContentType.Speech)!.Build());
            player.Prepared += (_, _) =>
            {
                if (_player != player) return;
                _prepared = true;
                UpdateTransform();
                player.SeekTo(1);
                UpdatePlayback();
            };
            player.Completion += (_, _) => { if (_player == player) VirtualView.OnEnded(); };
            player.Error += (_, e) => { e.Handled = true; if (_player == player) { _prepared = false; VirtualView.OnFailed(); } };
            player.SetDataSource(path);
            player.PrepareAsync();
        }
        catch (Exception) { ReleasePlayer(); VirtualView.OnFailed(); }
    }
    private void UpdateTransform()
    {
        if (!_prepared || _player is null || PlatformView.Width <= 0 || PlatformView.Height <= 0 || _player.VideoWidth <= 0 || _player.VideoHeight <= 0) return;
        var videoRatio = _player.VideoWidth / (float)_player.VideoHeight;
        var viewRatio = PlatformView.Width / (float)PlatformView.Height;
        using var transform = new Android.Graphics.Matrix();
        transform.SetScale(Math.Max(1, videoRatio / viewRatio), Math.Max(1, viewRatio / videoRatio), PlatformView.Width / 2f, PlatformView.Height / 2f);
        PlatformView.SetTransform(transform);
    }
    private void UpdatePlayback()
    {
        if (!_prepared || _player is null) return;
        if (VirtualView.IsPlaying) { if (_player.CurrentPosition >= _player.Duration - 100) _player.SeekTo(0); _player.Start(); }
        else if (_player.IsPlaying) _player.Pause();
    }
    public void Seek(double ms) { if (_prepared && _player is not null) _player.SeekTo((int)Math.Clamp(ms, 0, DurationMs)); }
    private void ReleasePlayer() { _prepared = false; _player?.Release(); _player?.Dispose(); _player = null; }
    protected override void DisconnectHandler(NativeVideoView platformView)
    {
        ReleasePlayer(); _surface?.Dispose(); _surface = null;
        platformView.SurfaceTextureListener = null; _listener?.Dispose(); _listener = null;
        base.DisconnectHandler(platformView);
    }
    private sealed class SurfaceListener(InlineNoteVideoHandler owner) : Java.Lang.Object, NativeVideoView.ISurfaceTextureListener
    {
        public void OnSurfaceTextureAvailable(Android.Graphics.SurfaceTexture surface, int width, int height)
        { owner._surface = new Android.Views.Surface(surface); owner.UpdateSource(); }
        public bool OnSurfaceTextureDestroyed(Android.Graphics.SurfaceTexture surface)
        { owner.ReleasePlayer(); owner._surface?.Dispose(); owner._surface = null; return true; }
        public void OnSurfaceTextureSizeChanged(Android.Graphics.SurfaceTexture surface, int width, int height) => owner.UpdateTransform();
        public void OnSurfaceTextureUpdated(Android.Graphics.SurfaceTexture surface) { }
    }
#elif IOS
    private AVFoundation.AVPlayer? _player;
    private AVFoundation.AVPlayerLayer? _layer;
    private Foundation.NSObject? _ended;
    public double PositionMs => SafeTime(_player?.CurrentTime.Seconds ?? 0);
    public double DurationMs => SafeTime(_player?.CurrentItem?.Duration.Seconds ?? 0);
    private static double SafeTime(double seconds) => double.IsFinite(seconds) ? Math.Max(0, seconds * 1000) : 0;
    protected override NativeVideoView CreatePlatformView() => new VideoContainer(this) { ClipsToBounds = true };
    private void UpdateSource()
    {
        ReleasePlayer();
        if (VirtualView.Source is not { } path) return;
        using var url = Foundation.NSUrl.FromFilename(path);
        var item = AVFoundation.AVPlayerItem.FromUrl(url);
        _player = new AVFoundation.AVPlayer(item);
        _layer = AVFoundation.AVPlayerLayer.FromPlayer(_player);
        _layer.VideoGravity = AVFoundation.AVLayerVideoGravity.ResizeAspectFill;
        _layer.Frame = PlatformView.Bounds;
        PlatformView.Layer.AddSublayer(_layer);
        _ended = Foundation.NSNotificationCenter.DefaultCenter.AddObserver(AVFoundation.AVPlayerItem.DidPlayToEndTimeNotification,
            _ => MainThread.BeginInvokeOnMainThread(VirtualView.OnEnded), item);
        UpdatePlayback();
    }
    private void UpdatePlayback() { if (VirtualView.IsPlaying) { if (DurationMs > 0 && PositionMs >= DurationMs - 100) Seek(0); _player?.Play(); } else _player?.Pause(); }
    public void Seek(double ms) => _player?.Seek(new CoreMedia.CMTime((long)Math.Max(0, ms), 1000));
    private void ReleasePlayer()
    {
        if (_ended is not null) { Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(_ended); _ended.Dispose(); _ended = null; }
        _player?.Pause(); _player?.Dispose(); _player = null;
        _layer?.RemoveFromSuperLayer(); _layer?.Dispose(); _layer = null;
    }
    protected override void DisconnectHandler(NativeVideoView platformView) { ReleasePlayer(); base.DisconnectHandler(platformView); }
    private sealed class VideoContainer(InlineNoteVideoHandler owner) : UIKit.UIView
    {
        public override void LayoutSubviews() { base.LayoutSubviews(); if (owner._layer is not null) owner._layer.Frame = Bounds; }
    }
#endif
}
