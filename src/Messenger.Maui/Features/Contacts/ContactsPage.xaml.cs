using Messenger.Contracts.Contacts;
using Messenger.Maui.Controls;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui.Features.Contacts;

public partial class ContactsPage : ContentPage
{
    private readonly ContactsViewModel _viewModel;
    private readonly IConversationApi _api;
    private readonly IServiceProvider _services;

    public ContactsPage(ContactsViewModel viewModel, IConversationApi api, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _api = api;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync(_viewModel.LoadAsync);
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        var query = SearchEntry.Text?.Trim();
        if (string.IsNullOrEmpty(query)) { OnCancelSearchClicked(sender, e); return; }
        await RunAsync(ct => _viewModel.SearchAsync(query, ct));
        ResultsList.IsVisible = CancelSearchButton.IsVisible = true;
        ContactsList.IsVisible = false;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.NewTextValue) && ResultsList is not null) OnCancelSearchClicked(sender, e);
    }

    private void OnCancelSearchClicked(object? sender, EventArgs e)
    {
        SearchEntry.Text = string.Empty;
        ResultsList.IsVisible = CancelSearchButton.IsVisible = false;
        ContactsList.IsVisible = true;
    }

    private async void OnContactSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ContactResponse contact) return;
        ContactsList.SelectedItem = null;
        await ShowContactAsync(contact);
    }

    private async void OnInfoClicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton { CommandParameter: ContactResponse contact }) await ShowContactAsync(contact);
    }

    private Task ShowContactAsync(ContactResponse contact) =>
        Navigation.PushAsync(new ContactDetailsPage(contact, _viewModel, () => OpenChatAsync(contact.UserId, DisplayValueConverter.Name(contact))));

    private async void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not UserSearchResponse user) return;
        ResultsList.SelectedItem = null;
        await RunAsync(_ => OpenChatAsync(user.UserId, DisplayValueConverter.Name(user)));
    }

    private async Task OpenChatAsync(Guid userId, string title)
    {
        var chat = await _api.CreateDirectAsync(userId);
        var session = await _services.GetRequiredService<ISecureSessionStore>().GetAsync();
        await Navigation.PushAsync(new ChatPage(
            new ChatViewModel(chat.Id, _api, _services.GetRequiredService<MediaComposerViewModel>()),
            _services.GetRequiredService<ChatSyncService>(), session?.Session.UserId ?? Guid.Empty, title));
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        try { await action(CancellationToken.None); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
