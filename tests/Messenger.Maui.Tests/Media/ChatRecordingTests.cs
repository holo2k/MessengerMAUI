using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Media;

public sealed class ChatRecordingTests
{
    [Theory]
    [InlineData(false, "voice")]
    [InlineData(true, "video_note")]
    public async Task Releasing_a_recording_uploads_one_file_with_duration_and_real_waveform(bool video, string type)
    {
        using var fixture = new Fixture();
        if (video) fixture.Vm.ToggleMode();
        MessageResponse? delivered = null;
        fixture.Vm.Sent += message => delivered = message;
        await fixture.Vm.StartAsync();
        await fixture.Vm.ReleaseAsync();
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(type, request.Type);
        Assert.Equal(2000, request.DurationMs);
        Assert.Equal(video ? Array.Empty<byte>() : new byte[] { 10, 60, 100 }, request.Waveform);
        Assert.Single(request.AttachmentObjectIds!);
        Assert.Null(request.Body);
        Assert.Equal(1, fixture.Uploader.Calls);
        Assert.Equal(1, fixture.Recorder.Deletes);
        Assert.Equal(ChatRecordingState.Idle, fixture.Vm.State);
        Assert.NotNull(delivered);
    }

    [Fact]
    public async Task Swiping_up_locks_recording_and_allows_review_before_sending()
    {
        using var fixture = new Fixture();
        await fixture.Vm.StartAsync();
        await fixture.Vm.DragAsync(0, -80);
        await fixture.Vm.ReleaseAsync();
        Assert.True(fixture.Vm.IsLocked);
        Assert.Equal(0, fixture.Recorder.Stops);
        Assert.Empty(fixture.Requests);
        await fixture.Vm.StopForReviewAsync();
        Assert.True(fixture.Vm.IsReview);
        Assert.NotNull(fixture.Vm.Draft);
        Assert.Empty(fixture.Requests);
        await fixture.Vm.SendAsync();
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task Canceling_and_short_takes_never_upload_or_send()
    {
        using var fixture = new Fixture();
        await fixture.Vm.StartAsync();
        await fixture.Vm.DragAsync(-90, 0);
        await fixture.Vm.ReleaseAsync();
        Assert.False(fixture.Vm.IsActive);
        Assert.Empty(fixture.Requests);
        fixture.Recorder.Duration = 200;
        await fixture.Vm.StartAsync();
        await fixture.Vm.ReleaseAsync();
        Assert.Equal(1, fixture.Recorder.Deletes);
        Assert.Equal(0, fixture.Uploader.Calls);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task Failed_send_keeps_the_draft_and_reuses_upload_and_message_id_on_retry()
    {
        using var fixture = new Fixture { FailSend = true };
        await fixture.Vm.StartAsync();
        await fixture.Vm.ReleaseAsync();
        Assert.True(fixture.Vm.IsReview);
        Assert.NotNull(fixture.Vm.Draft);
        Assert.NotNull(fixture.Vm.Error);
        Assert.Equal(0, fixture.Recorder.Deletes);
        fixture.FailSend = false;
        await fixture.Vm.SendAsync();
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Equal(fixture.Requests[0].ClientMessageId, fixture.Requests[1].ClientMessageId);
        Assert.Equal(fixture.Requests[0].AttachmentObjectIds, fixture.Requests[1].AttachmentObjectIds);
        Assert.Equal(1, fixture.Uploader.Calls);
        Assert.False(fixture.Vm.IsActive);
    }

    [Fact]
    public async Task Upload_failure_keeps_a_retryable_draft_and_denied_microphone_resets_recording()
    {
        using var fixture = new Fixture();
        fixture.Recorder.Deny = true;
        await fixture.Vm.StartAsync();
        Assert.False(fixture.Vm.IsActive);
        Assert.NotNull(fixture.Vm.Error);
        fixture.Recorder.Deny = false;
        fixture.Uploader.Fail = true;
        await fixture.Vm.StartAsync();
        await fixture.Vm.ReleaseAsync();
        Assert.True(fixture.Vm.IsReview);
        Assert.Empty(fixture.Requests);
        fixture.Uploader.Fail = false;
        await fixture.Vm.SendAsync();
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Releasing_during_camera_or_permission_startup_cancels_unless_lock_was_requested(bool lockRecording)
    {
        using var fixture = new Fixture();
        fixture.Recorder.WaitForStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = fixture.Vm.StartAsync();
        Assert.Equal(ChatRecordingState.Starting, fixture.Vm.State);
        if (lockRecording) await fixture.Vm.DragAsync(0, -80);
        var release = fixture.Vm.ReleaseAsync();
        fixture.Recorder.WaitForStart.SetResult();
        await Task.WhenAll(starting, release);
        Assert.Equal(lockRecording ? ChatRecordingState.Locked : ChatRecordingState.Idle, fixture.Vm.State);
        Assert.Empty(fixture.Requests);
        await fixture.Vm.CancelAsync();
    }

    [Fact]
    public async Task The_video_limit_stops_in_review_instead_of_leaving_the_camera_running()
    {
        using var fixture = new Fixture(61_000);
        fixture.Vm.ToggleMode();
        await fixture.Vm.StartAsync();
        await fixture.Vm.TickAsync();
        Assert.True(fixture.Vm.IsReview);
        Assert.False(fixture.Vm.ShowCamera);
        Assert.Equal(1, fixture.Recorder.Stops);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void Waveform_uses_pcm_amplitude_and_wav_header_matches_the_recorded_bytes()
    {
        Assert.Equal(0, VoiceWaveform.Level(new byte[] { 0, 0, 0, 0 }));
        Assert.Equal(100, VoiceWaveform.Level(new byte[] { 0, 0x80, 0xFF, 0x7F }));
        Assert.Equal(new byte[] { 60, 100 }, VoiceWaveform.Compress(new byte[] { 0, 60, 40, 100 }, 2));
        using var stream = new MemoryStream();
        VoiceWaveform.WriteWavHeader(stream, 16000, 32000);
        var bytes = stream.ToArray();
        Assert.Equal(44, bytes.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(32036, BitConverter.ToInt32(bytes, 4));
        Assert.Equal(32000, BitConverter.ToInt32(bytes, 40));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public Recorder Recorder { get; } = new();
        public Uploader Uploader { get; } = new();
        public List<SendMessageRequest> Requests { get; } = [];
        public bool FailSend { get; set; }
        public ChatRecordingViewModel Vm { get; }
        public Fixture(long elapsed = 0)
        {
            var chatId = Guid.NewGuid();
            _http = new HttpClient(new Handler(async request =>
            {
                var sent = (await request.Content!.ReadFromJsonAsync<SendMessageRequest>())!;
                Requests.Add(sent);
                if (FailSend) return new(HttpStatusCode.ServiceUnavailable);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new MessageResponse(Guid.NewGuid(), chatId, Guid.NewGuid(), 1,
                    sent.ClientMessageId, sent.Type, null, false, DateTimeOffset.UtcNow, null, null, DurationMs: sent.DurationMs, Waveform: sent.Waveform)) };
            })) { BaseAddress = new Uri("https://api.test/") };
            Vm = new ChatRecordingViewModel(chatId, Recorder, Uploader, new ConversationClient(_http), () => elapsed);
        }
        public void Dispose() => _http.Dispose();
    }
    private sealed class Recorder : IChatNoteRecorder
    {
        private ChatNoteKind _kind;
        public event Action<byte>? LevelChanged;
        public TaskCompletionSource? WaitForStart { get; set; }
        public bool Deny { get; set; }
        public long Duration { get; set; } = 2000;
        public int Stops { get; private set; }
        public int Deletes { get; private set; }
        public async Task StartAsync(ChatNoteKind kind, CancellationToken ct = default)
        {
            _kind = kind;
            if (Deny) throw new InvalidOperationException("Нет разрешения на микрофон");
            if (WaitForStart is not null) await WaitForStart.Task.WaitAsync(ct);
            LevelChanged?.Invoke(60);
        }
        public Task<RecordedChatNote> StopAsync(CancellationToken ct = default)
        {
            Stops++;
            var media = new MediaSelection(_kind == ChatNoteKind.Voice ? MediaKind.Audio : MediaKind.Video, "note.bin", "audio/wav", 3, Duration,
                () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
            return Task.FromResult(new RecordedChatNote(_kind, media, "draft.bin", Duration, _kind == ChatNoteKind.Voice ? [10, 60, 100] : []));
        }
        public Task CancelAsync() => Task.CompletedTask;
        public void Delete(RecordedChatNote note) => Deletes++;
    }
    private sealed class Uploader : IMediaUploader
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<StoredObjectResponse> UploadAsync(MediaSelection selection, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            if (Fail) throw new IOException("offline");
            return Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), "note.wav", "audio/wav", 3, "sha", DateTimeOffset.UtcNow));
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
}
