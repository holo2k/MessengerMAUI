using Messenger.Contracts.Contacts;
using Messenger.Maui.Controls;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public partial class NewChatPage : ContentPage
{
    private readonly IConversationApi _api;
    private readonly Func<IReadOnlyList<Guid>, string?, Task> _create;
    private IReadOnlyList<ContactResponse> _contacts = [];

    public NewChatPage(IConversationApi api, Func<IReadOnlyList<Guid>, string?, Task> create)
    {
        InitializeComponent();
        _api = api;
        _create = create;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { _contacts = await _api.GetContactsAsync(); ContactsList.ItemsSource = _contacts; }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.Trim() ?? "";
        ContactsList.ItemsSource = _contacts.Where(contact => DisplayValueConverter.Name(contact).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CreateButton.IsEnabled = e.CurrentSelection.Count > 0;
        GroupTitleEntry.IsVisible = e.CurrentSelection.Count > 1;
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var ids = ContactsList.SelectedItems.OfType<ContactResponse>().Select(contact => contact.UserId).ToArray();
        if (ids.Length == 0) return;
        if (ids.Length > 1 && string.IsNullOrWhiteSpace(GroupTitleEntry.Text))
        {
            GroupTitleEntry.Focus();
            return;
        }
        try { CreateButton.IsEnabled = false; await _create(ids, GroupTitleEntry.Text); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
        finally { CreateButton.IsEnabled = true; }
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
