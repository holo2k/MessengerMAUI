using System.Collections.ObjectModel;
using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Music;

public partial class MusicPickerPage : ContentPage
{
    private readonly IMusicApi _api;
    private readonly Func<MusicTrackResponse, Task> _send;
    private bool _loaded, _sending;
    private int _queryVersion;
    public ObservableCollection<MusicTrackResponse> Tracks { get; } = [];

    public MusicPickerPage(IMusicApi api, Func<MusicTrackResponse, Task> send)
    {
        _api = api;
        _send = send;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var version = ++_queryVersion;
        var query = Search.Text?.Trim();
        try
        {
            await TracksLoading.LoadAsync(async () =>
            {
                var rows = string.IsNullOrWhiteSpace(query)
                    ? await _api.LibraryAsync(CancellationToken.None) : await _api.SearchAsync(query, CancellationToken.None);
                if (version != _queryVersion) return;
                Tracks.Clear();
                foreach (var row in rows) Tracks.Add(row);
            }, replaceContent: true);
        }
        catch (Exception exception) { if (version == _queryVersion) await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }

    private async void OnSearchPressed(object? sender, EventArgs e) => await LoadAsync();
    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_loaded && string.IsNullOrWhiteSpace(e.NewTextValue)) await LoadAsync();
    }

    private async void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_sending || e.CurrentSelection.FirstOrDefault() is not MusicTrackResponse track) return;
        TrackChoices.SelectedItem = null;
        _sending = SendingOverlay.IsVisible = true;
        try { await _send(track); await Navigation.PopAsync(); }
        catch (Exception exception) { await DisplayAlertAsync("Не удалось отправить", exception.Message, "OK"); }
        finally { _sending = SendingOverlay.IsVisible = false; }
    }

    private async void OnBackClicked(object? sender, EventArgs e) { if (!_sending) await Navigation.PopAsync(); }
    protected override bool OnBackButtonPressed() => _sending || base.OnBackButtonPressed();
}
