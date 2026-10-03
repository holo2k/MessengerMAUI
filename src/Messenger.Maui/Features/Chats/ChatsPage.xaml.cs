using Messenger.Maui.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui.Features.Chats;

public partial class ChatsPage : ContentPage
{
    private readonly ChatsViewModel _viewModel;
    private readonly IConversationApi _api;
    private readonly IServiceProvider _services;

    public ChatsPage(ChatsViewModel viewModel, IConversationApi api, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _api = api;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SafeRunAsync(_viewModel.LoadAsync);
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await SafeRunAsync(_viewModel.LoadAsync);

    private async void OnNewChatClicked(object? sender, EventArgs e)
    {
        var raw = await DisplayPromptAsync("Новый чат", "ID контактов через запятую");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }
        try
        {
            var ids = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Guid.Parse).ToArray();
            var title = ids.Length > 1
                ? await DisplayPromptAsync("Группа", "Название группы")
                : null;
            await _viewModel.CreateChatAsync(ids, title);
            await _viewModel.LoadAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Ошибка", exception.Message, "OK");
        }
    }

    private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ChatListItem item)
        {
            return;
        }
        ChatsList.SelectedItems.Clear();
        var sync = _services.GetRequiredService<ChatSyncService>();
        await Navigation.PushAsync(new ChatPage(new ChatViewModel(item.Chat.Id, _api), sync));
    }

    private async void OnArchiveClicked(object? sender, EventArgs e) => await ApplyBulkAsync(ChatBulkAction.Archive);
    private async void OnReadClicked(object? sender, EventArgs e) => await ApplyBulkAsync(ChatBulkAction.MarkRead);
    private async void OnDeleteClicked(object? sender, EventArgs e) => await ApplyBulkAsync(ChatBulkAction.Delete);

    private async Task ApplyBulkAsync(ChatBulkAction action)
    {
        var ids = ChatsList.SelectedItems.Cast<ChatListItem>().Select(item => item.Chat.Id).ToArray();
        await SafeRunAsync(ct => _viewModel.ApplyBulkAsync(ids, action, ct));
        ChatsList.SelectedItems.Clear();
    }

    private async Task SafeRunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
