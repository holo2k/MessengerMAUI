using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public enum ChatRecordingState { Idle, Starting, Recording, Locked, Review, Sending }

public sealed class ChatRecordingViewModel(Guid chatId, IChatNoteRecorder recorder, IMediaUploader uploader, IConversationApi api, Func<long>? elapsedMilliseconds = null) : ObservableObject
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Stopwatch _elapsed = new();
    private ChatRecordingState _state;
    private ChatNoteKind _kind;
    private Guid _clientId;
    private StoredObjectResponse? _uploaded;
    private CancellationTokenSource? _startCancellation;
    private long _elapsedMs;
    private double _uploadProgress;
    private byte _level;
    private string? _error;
    private bool _lockAfterStart;
    public RecordedChatNote? Draft { get; private set; }
    public event Action<MessageResponse>? Sent;
    public ChatRecordingState State => _state;
    public ChatNoteKind Kind => _kind;
    public bool IsActive => State != ChatRecordingState.Idle;
    public bool IsCapturing => State is ChatRecordingState.Recording or ChatRecordingState.Locked;
    public bool IsLocked => State == ChatRecordingState.Locked;
    public bool IsVideo => Kind == ChatNoteKind.VideoNote;
    public bool IsReview => State == ChatRecordingState.Review;
    public bool IsSending => State == ChatRecordingState.Sending;
    public bool ShowCamera => IsVideo && State is ChatRecordingState.Starting or ChatRecordingState.Recording or ChatRecordingState.Locked;
    public bool ShowDraft => Draft is not null && State is ChatRecordingState.Review or ChatRecordingState.Sending;
    public long DurationMs => Draft?.DurationMs ?? _elapsedMs;
    public string DurationText => PlaybackTime.Format(DurationMs);
    public double LimitProgress => Math.Clamp(DurationMs / (IsVideo ? 60_000d : 600_000d), 0, 1);
    public double UploadProgress { get => _uploadProgress; private set => SetProperty(ref _uploadProgress, value); }
    public byte Level { get => _level; private set => SetProperty(ref _level, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public void ToggleMode()
    {
        if (IsActive) return;
        _kind = IsVideo ? ChatNoteKind.Voice : ChatNoteKind.VideoNote;
        NotifyState();
    }

    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (IsActive) return;
            Error = null; _elapsedMs = 0; Level = 0; _lockAfterStart = false;
            _clientId = Guid.NewGuid(); _uploaded = null;
            _startCancellation?.Dispose();
            _startCancellation = new();
            SetState(ChatRecordingState.Starting);
            recorder.LevelChanged += OnLevel;
            try
            {
                await recorder.StartAsync(Kind, _startCancellation.Token);
                _startCancellation.Token.ThrowIfCancellationRequested();
                _elapsed.Restart();
                SetState(_lockAfterStart ? ChatRecordingState.Locked : ChatRecordingState.Recording);
            }
            catch (OperationCanceledException) when (_startCancellation.IsCancellationRequested)
            { await recorder.CancelAsync(); recorder.LevelChanged -= OnLevel; SetState(ChatRecordingState.Idle); }
            catch (Exception exception)
            { await recorder.CancelAsync(); recorder.LevelChanged -= OnLevel; Error = exception.Message; SetState(ChatRecordingState.Idle); }
        }
        finally { _gate.Release(); }
    }

    private void OnLevel(byte value) => Level = value;

    public async Task TickAsync()
    {
        if (!IsCapturing) return;
        _elapsedMs = elapsedMilliseconds?.Invoke() ?? _elapsed.ElapsedMilliseconds;
        OnPropertyChanged(nameof(DurationMs)); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(LimitProgress));
        if (_elapsedMs >= (IsVideo ? 60_000 : 600_000)) await StopForReviewAsync();
    }

    public async Task DragAsync(double x, double y)
    {
        if (State is not (ChatRecordingState.Starting or ChatRecordingState.Recording)) return;
        if (x <= -80) await CancelAsync();
        else if (y <= -70) { _lockAfterStart = true; if (IsCapturing) SetState(ChatRecordingState.Locked); }
    }

    public Task ReleaseAsync() => State == ChatRecordingState.Locked || State == ChatRecordingState.Starting && _lockAfterStart ? Task.CompletedTask : SendAsync();

    public async Task StopForReviewAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task<bool> StopCoreAsync()
    {
        if (!IsCapturing) return Draft is not null;
        _elapsed.Stop();
        recorder.LevelChanged -= OnLevel;
        try
        {
            var note = await recorder.StopAsync();
            if (note.DurationMs < 500)
            { recorder.Delete(note); SetState(ChatRecordingState.Idle); return false; }
            Draft = note;
            SetState(ChatRecordingState.Review);
            return true;
        }
        catch (Exception exception)
        { await recorder.CancelAsync(); Error = exception.Message; SetState(ChatRecordingState.Idle); return false; }
    }

    public async Task SendAsync()
    {
        if (IsSending) return;
        // A quick release while permissions/camera startup are pending cancels the take.
        if (State == ChatRecordingState.Starting) { await CancelAsync(); return; }
        await _gate.WaitAsync();
        try
        {
            if (!await StopCoreAsync() || Draft is not { } note || IsSending) return;
            Error = null; UploadProgress = 0;
            SetState(ChatRecordingState.Sending);
            try
            {
                _uploaded ??= await uploader.UploadAsync(note.Media, new ProgressValue(value => UploadProgress = value));
                UploadProgress = 1;
                var sent = await api.SendWithAttachmentsAsync(chatId, _clientId, note.Kind == ChatNoteKind.Voice ? "voice" : "video_note", null,
                    [_uploaded.Id], durationMs: note.DurationMs, waveform: note.Waveform);
                if (sent.Attachments is not { Count: > 0 }) sent = sent with { Attachments = [_uploaded] };
                Sent?.Invoke(sent);
                Draft = null; _uploaded = null;
                SetState(ChatRecordingState.Idle);
                try { recorder.Delete(note); } catch (IOException) { /* The cache can be cleaned after playback releases its file. */ }
            }
            catch (Exception exception) { Error = exception.Message; SetState(ChatRecordingState.Review); }
        }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync()
    {
        _startCancellation?.Cancel();
        await _gate.WaitAsync();
        try
        {
            if (IsSending) return;
            await recorder.CancelAsync();
            recorder.LevelChanged -= OnLevel;
            _elapsed.Reset();
            if (Draft is { } note) recorder.Delete(note);
            Draft = null; _uploaded = null;
            SetState(ChatRecordingState.Idle);
        }
        finally { _gate.Release(); }
    }

    private void SetState(ChatRecordingState state) { _state = state; NotifyState(); }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(State), nameof(Kind), nameof(IsActive), nameof(IsCapturing), nameof(IsLocked), nameof(IsVideo),
                     nameof(IsReview), nameof(IsSending), nameof(ShowCamera), nameof(ShowDraft), nameof(Draft), nameof(DurationMs), nameof(DurationText), nameof(LimitProgress) })
            OnPropertyChanged(name);
    }
    private sealed class ProgressValue(Action<double> update) : IProgress<double> { public void Report(double value) => update(value); }
}
