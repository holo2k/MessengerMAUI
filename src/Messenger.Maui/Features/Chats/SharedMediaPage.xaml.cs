using Messenger.Contracts.Media;

namespace Messenger.Maui.Features.Chats;

public partial class SharedMediaPage : ContentPage
{
    private readonly Func<Task<IReadOnlyList<StoredObjectResponse>>> _load;
    private bool _loaded;
    public SharedMediaPage(Func<Task<IReadOnlyList<StoredObjectResponse>>> load)
    {
        InitializeComponent();
        _load = load;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        try
        {
            await MediaLoading.LoadAsync(async () => { MediaList.ItemsSource = await _load(); _loaded = true; });
        }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
