using Microsoft.Maui.Handlers;

namespace Messenger.Maui.Controls;

public sealed class MessageBubbleHandler : ContentViewHandler
{
#if ANDROID
    protected override Microsoft.Maui.Platform.ContentViewGroup CreatePlatformView() =>
        new PressableContentView(Context, () => (VirtualView as MessageBubble)?.RequestMessageActions(), IsInsideBubble)
        { CrossPlatformLayout = VirtualView };

    private bool IsInsideBubble(float x, float y)
    {
        var density = Context.Resources?.DisplayMetrics?.Density ?? 1;
        return VirtualView is MessageBubble { Content: { } content } && content.Bounds.Contains(x / density, y / density);
    }

    private sealed class PressableContentView : Microsoft.Maui.Platform.ContentViewGroup
    {
        private readonly Action _pressed;
        private readonly Func<float, float, bool> _inside;
        private readonly Java.Lang.Runnable _recognize;
        private readonly int _slop;
        private Android.Views.MotionEvent? _down;
        private bool _triggered;

        public PressableContentView(Android.Content.Context context, Action pressed, Func<float, float, bool> inside) : base(context)
        {
            _pressed = pressed;
            _inside = inside;
            _slop = Android.Views.ViewConfiguration.Get(context)!.ScaledTouchSlop;
            _recognize = new Java.Lang.Runnable(() =>
            {
                if (_down is null) return;
                _triggered = true;
                Parent?.RequestDisallowInterceptTouchEvent(true);
                using var cancel = Android.Views.MotionEvent.Obtain(_down)!;
                cancel.Action = Android.Views.MotionEventActions.Cancel;
                base.DispatchTouchEvent(cancel);
                PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
                _pressed();
            });
        }

        public override bool DispatchTouchEvent(Android.Views.MotionEvent? e)
        {
            if (e is null) return false;
            if (e.ActionMasked == Android.Views.MotionEventActions.Down)
            {
                Reset();
                if (_inside(e.GetX(), e.GetY()))
                {
                    _down = Android.Views.MotionEvent.Obtain(e);
                    PostDelayed(_recognize, Android.Views.ViewConfiguration.LongPressTimeout);
                }
            }
            if (e.ActionMasked == Android.Views.MotionEventActions.Move && _down is not null &&
                (Math.Abs(e.GetX() - _down.GetX()) > _slop || Math.Abs(e.GetY() - _down.GetY()) > _slop))
                CancelPending();
            var handled = _triggered || base.DispatchTouchEvent(e);
            // Keep receiving a text bubble's gesture even when no child handles the initial touch.
            if (e.ActionMasked == Android.Views.MotionEventActions.Down && _down is not null) handled = true;
            if (e.ActionMasked is Android.Views.MotionEventActions.Up or Android.Views.MotionEventActions.Cancel) Reset();
            return handled;
        }

        private void CancelPending() { RemoveCallbacks(_recognize); _down?.Dispose(); _down = null; }
        private void Reset() { CancelPending(); _triggered = false; Parent?.RequestDisallowInterceptTouchEvent(false); }
        protected override void OnDetachedFromWindow() { Reset(); base.OnDetachedFromWindow(); }
        protected override void Dispose(bool disposing) { if (disposing) { Reset(); _recognize.Dispose(); } base.Dispose(disposing); }
    }
#elif IOS
    private UIKit.UILongPressGestureRecognizer? _press;
    protected override void ConnectHandler(Microsoft.Maui.Platform.ContentView platformView)
    {
        base.ConnectHandler(platformView);
        _press = new UIKit.UILongPressGestureRecognizer(gesture =>
        {
            if (gesture.State == UIKit.UIGestureRecognizerState.Began) (VirtualView as MessageBubble)?.RequestMessageActions();
        }) { MinimumPressDuration = 0.5, CancelsTouchesInView = true };
        _press.ShouldBegin = gesture =>
        {
            var point = gesture.LocationInView(platformView);
            return VirtualView is MessageBubble { Content: { } content } && content.Bounds.Contains(point.X, point.Y);
        };
        platformView.AddGestureRecognizer(_press);
    }

    protected override void DisconnectHandler(Microsoft.Maui.Platform.ContentView platformView)
    {
        if (_press is not null) { platformView.RemoveGestureRecognizer(_press); _press.Dispose(); _press = null; }
        base.DisconnectHandler(platformView);
    }
#endif
}
