using Messenger.Contracts.Music;
using Messenger.Maui.Services;
using Messenger.Contracts.Realtime;

namespace Messenger.Maui.Features.Music;

public partial class MusicPage : ContentPage
{
    private readonly MusicViewModel _viewModel;
    private readonly IMusicFilePicker _musicFiles;
    private readonly IMediaUploader _uploader;
    private readonly IMusicRealtimeClient _realtime;
    private bool _isSearching;
    public bool IsSearching { get => _isSearching; private set { _isSearching = value; OnPropertyChanged(); } }
    public MusicPage(MusicViewModel viewModel, IMusicFilePicker musicFiles, IMediaUploader uploader, IMusicRealtimeClient realtime)
    { InitializeComponent(); BindingContext = _viewModel = viewModel; _musicFiles = musicFiles; _uploader = uploader; _realtime = realtime; _realtime.TrackStatusChanged += OnTrackStatusChangedAsync; }
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
        if (!UploadButton.IsEnabled) return;
        UploadButton.IsEnabled = false;
        try
        {
            await SafeAsync(async ct =>
            {
                var selection = await _musicFiles.PickAsync(ct);
                if (selection is null) return;
                var title = await DisplayPromptAsync("Трек", "Название", initialValue: Path.GetFileNameWithoutExtension(selection.FileName));
                if (string.IsNullOrWhiteSpace(title)) return;
                var artist = await DisplayPromptAsync("Трек", "Исполнитель");
                if (string.IsNullOrWhiteSpace(artist)) return;
                UploadButton.Text = "Загрузка…";
                var stored = await _uploader.UploadAsync(selection, null, ct);
                await _viewModel.UploadAsync(stored.Id, title.Trim(), artist.Trim(), selection.DurationMs!.Value, null, ct);
            });
        }
        finally
        {
            UploadButton.Text = "Загрузить";
            UploadButton.IsEnabled = true;
        }
    }
    private async Task SafeAsync(Func<CancellationToken, Task> action) { try { await action(CancellationToken.None); } catch (Exception ex) { await DisplayAlertAsync("Ошибка", ex.Message, "OK"); } }
}
