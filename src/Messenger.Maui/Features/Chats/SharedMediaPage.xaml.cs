using Messenger.Contracts.Media;

namespace Messenger.Maui.Features.Chats;

public partial class SharedMediaPage : ContentPage
{
    public SharedMediaPage(IEnumerable<StoredObjectResponse> media)
    {
        InitializeComponent();
        MediaList.ItemsSource = media;
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
