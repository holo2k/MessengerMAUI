using Messenger.Contracts.Realtime;
using Messenger.Maui.Features.Music;
using Messenger.Maui.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui.Features.Chats;

public partial class SharedMediaPage : ContentPage
{
    private readonly Guid _chatId;
    private readonly SharedMediaViewModel _viewModel;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private IChatRealtimeClient? _realtime;
    private CancellationTokenSource? _lifetime;
    private bool _opening;
    public double TileSize { get; private set; } = 120;

    public SharedMediaPage(Guid chatId, IConversationApi api)
    {
        _chatId = chatId;
        InitializeComponent();
        BindingContext = _viewModel = new SharedMediaViewModel(chatId, api);
        UpdateTabs();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _lifetime?.Dispose();
        _lifetime = new();
        _realtime = Handler?.MauiContext?.Services.GetService<IChatRealtimeClient>();
        if (_realtime is not null)
        {
            _realtime.MessageCreated += OnCreated;
            _realtime.MessageUpdated += OnUpdated;
            _realtime.MessageDeleted += OnDeleted;
            _realtime.MessageHidden += OnHidden;
            _realtime.Reconnected += ReloadAsync;
        }
        await MediaLoading.LoadAsync(ReloadAsync);
    }

    protected override void OnDisappearing()
    {
        _lifetime?.Cancel();
        if (_realtime is not null)
        {
            _realtime.MessageCreated -= OnCreated;
            _realtime.MessageUpdated -= OnUpdated;
            _realtime.MessageDeleted -= OnDeleted;
            _realtime.MessageHidden -= OnHidden;
            _realtime.Reconnected -= ReloadAsync;
        }
        base.OnDisappearing();
    }

    private async Task ReloadAsync()
    {
        if (_lifetime is not { } lifetime) return;
        var ct = lifetime.Token;
        try
        {
            await _reloadLock.WaitAsync(ct);
            try { await MainThread.InvokeOnMainThreadAsync(() => _viewModel.LoadAsync(ct)); }
            finally { _reloadLock.Release(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { if (!ct.IsCancellationRequested) await MainThread.InvokeOnMainThreadAsync(() => DisplayAlertAsync("Вложения", exception.Message, "OK")); }
    }

    private Task OnCreated(MessageCreatedEvent value) => value.ChatId == _chatId ? ReloadAsync() : Task.CompletedTask;
    private Task OnUpdated(MessageUpdatedEvent value) => value.ChatId == _chatId ? ReloadAsync() : Task.CompletedTask;
    private Task OnDeleted(MessageDeletedEvent value) => RemoveAsync(value.ChatId, value.MessageId);
    private Task OnHidden(MessageHiddenEvent value) => RemoveAsync(value.ChatId, value.MessageId);
    private Task RemoveAsync(Guid chatId, Guid id) => chatId == _chatId ? MainThread.InvokeOnMainThreadAsync(() => _viewModel.RemoveMessage(id)) : Task.CompletedTask;

    private void OnMediaSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not Grid grid || grid.Width <= 0) return;
        TileSize = Math.Max(1, (grid.Width - 2) / 3);
        OnPropertyChanged(nameof(TileSize));
    }

    private void OnFilterClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string name } && Enum.TryParse<SharedMediaFilter>(name, out var filter))
        { _viewModel.Filter = filter; UpdateTabs(); }
    }
    private void UpdateTabs()
    {
        foreach (var tab in new[] { MediaTab, AudioTab, FilesTab, LinksTab })
            tab.BackgroundColor = tab.CommandParameter?.ToString() == _viewModel.Filter.ToString() ? Color.FromArgb("#D7EAFF") : Colors.Transparent;
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (_opening || e.Parameter is not SharedMediaItem item) return;
        _opening = true;
        try
        {
            if (item.Url is { } url) await Launcher.Default.OpenAsync(url);
            else if (item.Attachment is { } file)
                await AttachmentFile.OpenAsync(file, Handler!.MauiContext!.Services.GetRequiredService<IMessageAttachmentClient>());
            else if (item.Track is { } track && (Shell.Current as AppShell)?.Music is { } player)
            {
                if (player.CurrentTrack?.Id != track.Id)
                    await player.PlayFromQueueAsync(track, _viewModel.Items.Select(value => value.Track).OfType<Messenger.Contracts.Music.MusicTrackResponse>(), "Аудио чата", CancellationToken.None);
                await MusicPlayerPage.OpenAsync(Navigation, player);
            }
        }
        catch (Exception exception) { await DisplayAlertAsync("Вложение", exception.Message, "OK"); }
        finally { _opening = false; }
    }
    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
