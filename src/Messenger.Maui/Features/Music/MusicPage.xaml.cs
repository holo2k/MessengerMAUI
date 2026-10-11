using Messenger.Contracts.Music;
using Messenger.Maui.Services;
using Messenger.Contracts.Realtime;

namespace Messenger.Maui.Features.Music;

public partial class MusicPage : ContentPage
{
    private readonly MusicViewModel _viewModel;
    private readonly IMediaCaptureService _capture;
    private readonly IMediaUploader _uploader;
    private readonly IMusicRealtimeClient _realtime;
    private bool _isSearching;
    public bool IsSearching { get => _isSearching; private set { _isSearching = value; OnPropertyChanged(); } }
    public MusicPage(MusicViewModel viewModel, IMediaCaptureService capture, IMediaUploader uploader, IMusicRealtimeClient realtime)
    { InitializeComponent(); BindingContext = _viewModel = viewModel; _capture = capture; _uploader = uploader; _realtime = realtime; _realtime.TrackStatusChanged += OnTrackStatusChangedAsync; }
    protected override async void OnAppearing() { base.OnAppearing(); await SafeAsync(_viewModel.LoadLibraryAsync); await SafeAsync(_realtime.ConnectAsync); }
    private Task OnTrackStatusChangedAsync(MusicTrackStatusChangedEvent value) => value.Status is "blocked" or "deleted"
        ? MainThread.InvokeOnMainThreadAsync(() => _viewModel.OnTrackBlockedAsync(value.TrackId, value.Reason))
        : Task.CompletedTask;
    private async void OnSearchClicked(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.SearchText)) { TracksList.ItemsSource = _viewModel.Library; IsSearching = false; return; }
        await SafeAsync(_viewModel.SearchAsync);
        IsSearching = true;
        EmptyMusicLabel.Text = "Ничего не найдено";
        TracksList.ItemsSource = _viewModel.SearchResults;
    }
    private void OnSearchTextChanged(object? s, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.NewTextValue) && TracksList is not null)
        {
            TracksList.ItemsSource = _viewModel.Library;
            IsSearching = false;
            EmptyMusicLabel.Text = "Ваша музыка";
        }
    }
    private async void OnTrackSelected(object? s, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MusicTrackResponse track) return;
        TracksList.SelectedItem = null;
        await SafeAsync(ct => _viewModel.PlayAsync(track, ct));
    }
    private async void OnAddClicked(object? s, EventArgs e) { if ((s as ImageButton)?.CommandParameter is MusicTrackResponse t) await SafeAsync(ct => _viewModel.AddAsync(t, ct)); }
    private async void OnDownloadClicked(object? s, EventArgs e) { if ((s as ImageButton)?.CommandParameter is MusicTrackResponse t) await SafeAsync(ct => _viewModel.DownloadAsync(t, ct)); }
    private void OnPauseClicked(object? s, EventArgs e) => _viewModel.TogglePause();
    private async void OnUploadClicked(object? s, EventArgs e)
    {
        var accepted = await DisplayAlertAsync("Права на музыку", "Загружая трек, вы подтверждаете наличие прав. По обоснованному требованию правообладателя трек может быть заблокирован или удалён.", "Подтверждаю", "Отмена");
        if (!accepted) return;
        var picked = await _capture.AcquireAsync(MediaKind.Audio);
        if (picked.Selection is null) return;
        var title = await DisplayPromptAsync("Трек", "Название"); var artist = await DisplayPromptAsync("Трек", "Исполнитель");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return;
        await SafeAsync(async ct => { var stored = await _uploader.UploadAsync(picked.Selection, null, ct); await _viewModel.UploadAsync(stored.Id, title, artist, picked.Selection.DurationMs ?? 1, null, true, ct); });
    }
    private async Task SafeAsync(Func<CancellationToken, Task> action) { try { await action(CancellationToken.None); } catch (Exception ex) { await DisplayAlertAsync("Ошибка", ex.Message, "OK"); } }
}
