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
    public Guid? CurrentTrackId => _viewModel?.CurrentTrack?.Id;
    public MusicPage(MusicViewModel viewModel, IMusicFilePicker musicFiles, IMediaUploader uploader, IMusicRealtimeClient realtime)
    {
        InitializeComponent(); BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MusicViewModel.CurrentTrack)) OnPropertyChanged(nameof(CurrentTrackId)); };
        _musicFiles = musicFiles; _uploader = uploader; _realtime = realtime;
        _realtime.TrackStatusChanged += OnTrackStatusChangedAsync;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); _viewModel.RefreshPlayback();
        await SafeAsync(_viewModel.LoadLibraryAsync); await SafeAsync(_realtime.ConnectAsync);
    }
    private Task OnTrackStatusChangedAsync(MusicTrackStatusChangedEvent value) => value.Status is "blocked" or "deleted"
        ? MainThread.InvokeOnMainThreadAsync(() => _viewModel.OnTrackBlockedAsync(value.TrackId, value.Reason))
        : Task.CompletedTask;
    private async void OnSearchClicked(object? s, EventArgs e) => await SearchVisibleAsync();
    private async Task SearchVisibleAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.SearchText)) { ShowLibrary(); return; }
        var query = _viewModel.SearchText.Trim();
        await SafeAsync(_viewModel.SearchAsync);
        if (_viewModel.SearchText.Trim() != query) return;
        _isSearching = true;
        SectionTitle.Text = "Результаты поиска";
        EmptyMusicLabel.Text = "Ничего не найдено";
        TracksList.ItemsSource = _viewModel.SearchResults;
    }
    private async void OnSearchTextChanged(object? s, TextChangedEventArgs e)
    {
        if (_viewModel is null || TracksList is null) return;
        if (string.IsNullOrWhiteSpace(e.NewTextValue) && TracksList is not null)
        {
            ShowLibrary();
        }
        else await SearchVisibleAsync();
    }
    private async void OnTrackSelected(object? s, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MusicTrackResponse track) return;
        TracksList.SelectedItem = null;
        await StartSelectedTrackAsync(track);
    }
    private async void OnAddClicked(object? s, EventArgs e) { if ((s as ImageButton)?.CommandParameter is MusicTrackResponse t) await SafeAsync(ct => _viewModel.AddAsync(t, ct)); }
    private async void OnDownloadClicked(object? s, EventArgs e) { if ((s as ImageButton)?.CommandParameter is MusicTrackResponse t) await SafeAsync(ct => _viewModel.DownloadAsync(t, ct)); }
    private void ShowLibrary()
    {
        _viewModel.CancelSearch();
        TracksList.ItemsSource = _viewModel.Library; _isSearching = false;
        SectionTitle.Text = "Моя музыка";
        EmptyMusicLabel.Text = "Ваша музыка начинается здесь";
    }
    private async Task StartSelectedTrackAsync(MusicTrackResponse track)
    {
        if (_viewModel.CurrentTrack?.Id == track.Id)
            await MusicPlayerPage.OpenAsync(Navigation, _viewModel);
        else
            await PlayFromVisibleQueueAsync(track);
    }

    private Task PlayFromVisibleQueueAsync(MusicTrackResponse track) =>
        _viewModel.PlayFromQueueAsync(track, _isSearching ? _viewModel.SearchResults : _viewModel.Library,
            _isSearching ? $"Поиск: {_viewModel.SearchText}" : "Моя музыка");
    private async void OnPlayAllClicked(object? s, EventArgs e)
    {
        var tracks = _isSearching ? _viewModel.SearchResults : _viewModel.Library;
        if (tracks.FirstOrDefault() is { } track) await PlayFromVisibleQueueAsync(track);
    }
    private async void OnTrackMenuClicked(object? s, EventArgs e)
    {
        if ((s as ImageButton)?.CommandParameter is MusicTrackResponse track) await ShowTrackMenuAsync(track);
    }
    private async Task ShowTrackMenuAsync(MusicTrackResponse track)
    {
        var saved = _viewModel.Library.Any(x => x.Id == track.Id);
        var libraryAction = saved ? "Убрать из моей музыки" : "Добавить в мою музыку";
        var action = await DisplayActionSheetAsync(track.Title, "Отмена", null, libraryAction, "Скачать трек");
        if (action == libraryAction) await SafeAsync(ct => saved ? _viewModel.RemoveAsync(track, ct) : _viewModel.AddAsync(track, ct));
        if (action == "Скачать трек") await SafeAsync(ct => _viewModel.DownloadAsync(track, ct));
    }
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
