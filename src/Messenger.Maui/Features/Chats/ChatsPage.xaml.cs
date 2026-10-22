using Messenger.Contracts.Chats;
using Messenger.Maui.Controls;
using Messenger.Maui.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui.Features.Chats;

public partial class ChatsPage : ContentPage
{
    private readonly ChatsViewModel _viewModel;
    private readonly IConversationApi _api;
    private readonly IServiceProvider _services;
    private readonly ChatListSyncService _listSync;
    private bool _listSyncAttached;
    private bool _isEditing;

    public ChatsPage(
        ChatsViewModel viewModel,
        IConversationApi api,
        IServiceProvider services,
        ChatListSyncService listSync)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _api = api;
        _services = services;
        _listSync = listSync;
    }

    public void SetMiniPlayer(MiniPlayerView miniPlayer)
    {
        MiniPlayerHost.Content = miniPlayer;
        MiniPlayerHost.SetBinding(IsVisibleProperty, new Binding(nameof(MiniPlayerView.IsVisible), source: miniPlayer));
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set { _isEditing = value; OnPropertyChanged(); }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var session = await _services.GetRequiredService<ISecureSessionStore>().GetAsync();
        _viewModel.SetCurrentUser(session?.Session.UserId ?? Guid.Empty);
        AttachListSync();
        await ChatsLoading.LoadAsync(async () =>
        {
            await SafeRunAsync(_listSync.StartAsync, suppressUnauthorized: true);
            await SafeRunAsync(_viewModel.LoadAsync, suppressUnauthorized: true);
        });
    }

    protected override void OnDisappearing()
    {
        DetachListSync();
        _listSync.Stop();
        base.OnDisappearing();
    }

    private void AttachListSync()
    {
        if (_listSyncAttached)
        {
            return;
        }

        _listSync.ChatChanged += OnChatChangedAsync;
        _listSync.SummaryChanged += OnSummaryChangedAsync;
        _listSync.ReloadRequested += OnReloadRequestedAsync;
        _listSyncAttached = true;
    }

    private void DetachListSync()
    {
        if (!_listSyncAttached)
        {
            return;
        }

        _listSync.ChatChanged -= OnChatChangedAsync;
        _listSync.SummaryChanged -= OnSummaryChangedAsync;
        _listSync.ReloadRequested -= OnReloadRequestedAsync;
        _listSyncAttached = false;
    }

    private Task OnChatChangedAsync(ChatResponse chat, Messenger.Contracts.Realtime.MessageCreatedEvent _) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.Upsert(chat, incrementUnread: true));

    private Task OnSummaryChangedAsync(ChatResponse chat) =>
        MainThread.InvokeOnMainThreadAsync(() => _viewModel.Upsert(chat, incrementUnread: false));

    private Task OnReloadRequestedAsync() =>
        MainThread.InvokeOnMainThreadAsync(() => SafeRunAsync(_viewModel.LoadAsync));

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await SafeRunAsync(_viewModel.LoadAsync);
        ChatRefresh.IsRefreshing = false;
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        IsEditing = !IsEditing;
        EditButton.Text = IsEditing ? "Готово" : "Ред.";
        SmallTitle.IsVisible = NewChatButton.IsVisible = !IsEditing;
        LargeTitle.IsVisible = BulkBar.IsVisible = IsEditing;
        Shell.SetTabBarIsVisible(this, !IsEditing);
        ChatsList.SelectedItems.Clear();
    }

    private async void OnNewChatClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new NewChatPage(_api, async (ids, title) =>
        {
            var chat = await _viewModel.CreateChatAsync(ids, title);
            await Navigation.PopAsync();
            await OpenChatAsync(new ChatListItem(chat, 0));
        }));

    private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
    {
        ArchiveButton.IsEnabled = ReadButton.IsEnabled = DeleteButton.IsEnabled = e.CurrentSelection.Count > 0;
        if (IsEditing || e.CurrentSelection.FirstOrDefault() is not ChatListItem item) return;
        ChatsList.SelectedItems.Clear();
        await SafeRunAsync(_ => OpenChatAsync(item));
    }

    private async Task OpenChatAsync(ChatListItem item)
    {
        var sync = _services.GetRequiredService<ChatSyncService>();
        var media = _services.GetRequiredService<MediaComposerViewModel>();
        var store = _services.GetRequiredService<ISecureSessionStore>();
        var session = await store.GetAsync();
        await Navigation.PushAsync(new ChatPage(new ChatViewModel(item.Chat.Id, _api, media, _services.GetRequiredService<MessageForwarder>()), sync,
            item.Chat, session?.Session.UserId ?? Guid.Empty, OpenPeerProfileAsync));
    }

    private Task OpenPeerProfileAsync(Guid userId) =>
        Navigation.PushAsync(new Features.Contacts.ContactDetailsPage(
            userId,
            _services.GetRequiredService<Features.Contacts.UserDetailsViewModel>(),
            () => Navigation.PopAsync()));

    private async void OnArchiveClicked(object? sender, EventArgs e) => await ApplyBulkAsync(ChatBulkAction.Archive);
    private async void OnReadClicked(object? sender, EventArgs e) => await ApplyBulkAsync(ChatBulkAction.MarkRead);
    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Удалить выбранные чаты?", "Чаты будут скрыты из вашего списка.", "Удалить", "Отмена"))
            await ApplyBulkAsync(ChatBulkAction.Delete);
    }

    private async Task ApplyBulkAsync(ChatBulkAction action)
    {
        var ids = ChatsList.SelectedItems.Cast<ChatListItem>().Select(item => item.Chat.Id).ToArray();
        await SafeRunAsync(async ct =>
        {
            await _viewModel.ApplyBulkAsync(ids, action, ct);
            await _viewModel.LoadAsync(ct);
        });
        ChatsList.SelectedItems.Clear();
    }

    private async Task SafeRunAsync(Func<CancellationToken, Task> action, bool suppressUnauthorized = false)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) when (suppressUnauthorized && ExceptionDisplay.IsUnauthorized(exception))
        {
            System.Diagnostics.Debug.WriteLine("Startup authorization is handled by session recovery.");
        }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
