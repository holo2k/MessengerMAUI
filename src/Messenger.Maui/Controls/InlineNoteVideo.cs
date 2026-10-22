namespace Messenger.Maui.Controls;

public sealed class InlineNoteVideo : View
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(nameof(Source), typeof(string), typeof(InlineNoteVideo), null);
    public static readonly BindableProperty IsPlayingProperty = BindableProperty.Create(nameof(IsPlaying), typeof(bool), typeof(InlineNoteVideo), false);
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public double PositionMs => (Handler as InlineNoteVideoHandler)?.PositionMs ?? 0;
    public double DurationMs => (Handler as InlineNoteVideoHandler)?.DurationMs ?? 0;
    public event EventHandler? Ended;
    public event EventHandler? Failed;
    public void Seek(double ms) => (Handler as InlineNoteVideoHandler)?.Seek(ms);
    internal void OnEnded() { IsPlaying = false; Ended?.Invoke(this, EventArgs.Empty); }
    internal void OnFailed() { IsPlaying = false; Failed?.Invoke(this, EventArgs.Empty); }
}
