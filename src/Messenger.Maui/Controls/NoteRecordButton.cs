namespace Messenger.Maui.Controls;

public sealed class RecordingDragEventArgs(double x, double y) : EventArgs
{
    public double X { get; } = x;
    public double Y { get; } = y;
}

public sealed class NoteRecordButton : ImageButton
{
    public NoteRecordButton()
    {
        WidthRequest = HeightRequest = 44; Padding = 10; BackgroundColor = Colors.Transparent; Aspect = Aspect.AspectFit;
        Clicked += (_, _) => TapMode();
    }
    public event EventHandler? ModeTapped;
    public event EventHandler? HoldStarted;
    public event EventHandler<RecordingDragEventArgs>? HoldDragged;
    public event EventHandler? HoldReleased;
    public event EventHandler? HoldCanceled;
    internal void TapMode() => ModeTapped?.Invoke(this, EventArgs.Empty);
    internal void BeginHold() => HoldStarted?.Invoke(this, EventArgs.Empty);
    internal void DragHold(double x, double y) => HoldDragged?.Invoke(this, new(x, y));
    internal void EndHold() => HoldReleased?.Invoke(this, EventArgs.Empty);
    internal void CancelHold() => HoldCanceled?.Invoke(this, EventArgs.Empty);
}
