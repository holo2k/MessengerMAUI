using System.Collections.ObjectModel;
using Messenger.Contracts.Messages;
using Messenger.Maui.Controls;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public partial class MessageForwardPage : ContentPage
{
    private readonly IConversationApi _api;
    private readonly Func<Guid, Guid, Task> _forward;
    private readonly Dictionary<Guid, Guid> _clientIds = [];
    private bool _loaded, _sending;
    public ObservableCollection<ChatListItem> Chats { get; } = [];
    public string Preview { get; }

    public MessageForwardPage(MessageResponse message, IConversationApi api, Func<Guid, Guid, Task> forward)
    {
        _api = api;
        _forward = forward;
        Preview = DisplayValueConverter.MessageText(message);
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        try
        {
            await ChatsLoading.LoadAsync(async () =>
            {
                var chats = await _api.GetChatsAsync();
                Chats.Clear();
                foreach (var chat in chats.Where(chat => !chat.IsHidden)) Chats.Add(new ChatListItem(chat, 0));
                _loaded = true;
            });
        }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }

    private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_sending || e.CurrentSelection.FirstOrDefault() is not ChatListItem chat) return;
        ChatChoices.SelectedItem = null;
        _sending = SendingOverlay.IsVisible = true;
        if (!_clientIds.TryGetValue(chat.Chat.Id, out var clientId)) _clientIds[chat.Chat.Id] = clientId = Guid.NewGuid();
        try { await _forward(chat.Chat.Id, clientId); await Navigation.PopAsync(); }
        catch (Exception exception) { await DisplayAlertAsync("Не удалось переслать", exception.Message, "OK"); }
        finally { _sending = SendingOverlay.IsVisible = false; }
    }

    private async void OnBackClicked(object? sender, EventArgs e) { if (!_sending) await Navigation.PopAsync(); }
    protected override bool OnBackButtonPressed() => _sending || base.OnBackButtonPressed();
}
