using System.Collections.ObjectModel;
using Messenger.Contracts.Music;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Music;

public partial class MusicSharePage : ContentPage
{
    private readonly IConversationApi _conversations;
    private readonly IMusicApi _music;
    private readonly Dictionary<Guid, Guid> _clientMessageIds = [];
    private bool _loaded, _sending;
    public MusicTrackResponse Track { get; }
    public ObservableCollection<ChatListItem> Chats { get; } = [];

    public MusicSharePage(MusicTrackResponse track, IConversationApi conversations, IMusicApi music)
    {
        Track = track;
        _conversations = conversations;
        _music = music;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        try
        {
            var chats = await _conversations.GetChatsAsync();
            Chats.Clear();
            foreach (var chat in chats.Where(chat => !chat.IsHidden)) Chats.Add(new ChatListItem(chat, 0));
            _loaded = true;
        }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }

    private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_sending || e.CurrentSelection.FirstOrDefault() is not ChatListItem chat) return;
        ChatChoices.SelectedItem = null;
        _sending = SendingOverlay.IsVisible = true;
        if (!_clientMessageIds.TryGetValue(chat.Chat.Id, out var clientId))
            _clientMessageIds[chat.Chat.Id] = clientId = Guid.NewGuid();
        try
        {
            await _music.ShareAsync(Track.Id, chat.Chat.Id, clientId, CancellationToken.None);
            await Navigation.PopModalAsync(animated: false);
        }
        catch (Exception exception) { await DisplayAlertAsync("Не удалось отправить", exception.Message, "OK"); }
        finally { _sending = SendingOverlay.IsVisible = false; }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (!_sending) await Navigation.PopModalAsync(animated: false);
    }

    protected override bool OnBackButtonPressed() => _sending || base.OnBackButtonPressed();
}
