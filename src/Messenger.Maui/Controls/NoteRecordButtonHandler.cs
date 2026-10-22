using Microsoft.Maui.Handlers;

namespace Messenger.Maui.Controls;

public sealed class NoteRecordButtonHandler : ImageButtonHandler
{
#if ANDROID
    private Java.Lang.Runnable? _hold;
    private bool _holding, _down;
    private float _x, _y;
    protected override void ConnectHandler(Google.Android.Material.ImageView.ShapeableImageView platformView)
    {
        base.ConnectHandler(platformView);
        _hold = new Java.Lang.Runnable(() =>
        {
            if (!_down) return;
            _holding = true;
            platformView.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
            (VirtualView as NoteRecordButton)?.BeginHold();
        });
        platformView.Touch += OnTouch;
    }
    private void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        if (e.Event is not { } touch || VirtualView is not NoteRecordButton button) return;
        e.Handled = true;
        var density = Context.Resources?.DisplayMetrics?.Density ?? 1;
        switch (touch.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                _down = true; _holding = false; _x = touch.RawX; _y = touch.RawY;
                PlatformView.Parent?.RequestDisallowInterceptTouchEvent(true);
                PlatformView.PostDelayed(_hold!, 250);
                break;
            case Android.Views.MotionEventActions.Move:
                if (_holding) button.DragHold((touch.RawX - _x) / density, (touch.RawY - _y) / density);
                break;
            case Android.Views.MotionEventActions.Up:
                PlatformView.RemoveCallbacks(_hold!);
                if (_down) { if (_holding) button.EndHold(); else button.TapMode(); }
                _down = _holding = false;
                PlatformView.Parent?.RequestDisallowInterceptTouchEvent(false);
                break;
            case Android.Views.MotionEventActions.Cancel:
                PlatformView.RemoveCallbacks(_hold!);
                if (_down) button.CancelHold();
                _down = _holding = false;
                PlatformView.Parent?.RequestDisallowInterceptTouchEvent(false);
                break;
        }
    }
    protected override void DisconnectHandler(Google.Android.Material.ImageView.ShapeableImageView platformView)
    {
        platformView.Touch -= OnTouch;
        if (_hold is not null) { platformView.RemoveCallbacks(_hold); _hold.Dispose(); _hold = null; }
        if (_down) (VirtualView as NoteRecordButton)?.CancelHold();
        _down = _holding = false;
        base.DisconnectHandler(platformView);
    }
#elif IOS
    private UIKit.UILongPressGestureRecognizer? _hold;
    private UIKit.UITapGestureRecognizer? _tap;
    private CoreGraphics.CGPoint _start;
    protected override void ConnectHandler(UIKit.UIButton platformView)
    {
        base.ConnectHandler(platformView);
        _hold = new UIKit.UILongPressGestureRecognizer(gesture =>
        {
            if (VirtualView is not NoteRecordButton button) return;
            var point = gesture.LocationInView(platformView.Window);
            switch (gesture.State)
            {
                case UIKit.UIGestureRecognizerState.Began:
                    _start = point; button.BeginHold(); break;
                case UIKit.UIGestureRecognizerState.Changed:
                    button.DragHold(point.X - _start.X, point.Y - _start.Y); break;
                case UIKit.UIGestureRecognizerState.Ended: button.EndHold(); break;
                case UIKit.UIGestureRecognizerState.Cancelled: button.CancelHold(); break;
            }
        }) { MinimumPressDuration = 0.25, CancelsTouchesInView = true };
        _tap = new UIKit.UITapGestureRecognizer(() => (VirtualView as NoteRecordButton)?.TapMode());
        _tap.RequireGestureRecognizerToFail(_hold);
        platformView.AddGestureRecognizer(_hold); platformView.AddGestureRecognizer(_tap);
    }
    protected override void DisconnectHandler(UIKit.UIButton platformView)
    {
        if (_hold is not null) { platformView.RemoveGestureRecognizer(_hold); _hold.Dispose(); _hold = null; }
        if (_tap is not null) { platformView.RemoveGestureRecognizer(_tap); _tap.Dispose(); _tap = null; }
        base.DisconnectHandler(platformView);
    }
#endif
}
