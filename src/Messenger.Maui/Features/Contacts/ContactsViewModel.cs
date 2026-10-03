using Messenger.Contracts.Contacts;
using Messenger.Maui.Services;
using System.Collections.ObjectModel;

namespace Messenger.Maui.Features.Contacts;

public sealed class ContactsViewModel(
    IConversationApi api,
    Messenger.Maui.Services.IPhoneDialer dialer)
{
    public ObservableCollection<ContactResponse> Contacts { get; } = [];
    public ObservableCollection<UserSearchResponse> SearchResults { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Contacts.Clear();
        foreach (var contact in await api.GetContactsAsync(cancellationToken))
        {
            Contacts.Add(contact);
        }
    }

    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchResults.Clear();
        foreach (var user in await api.SearchUsersAsync(query, cancellationToken))
        {
            SearchResults.Add(user);
        }
    }
    public Task<ContactResponse> UpdateContactAsync(
        ContactResponse contact,
        string? localFirstName,
        string? localLastName,
        bool muted,
        CancellationToken cancellationToken = default) =>
        api.UpdateContactAsync(contact.Id, new UpdateContactRequest(localFirstName, localLastName, muted), cancellationToken);

    public Task CallAsync(ContactResponse contact)
    {
        if (string.IsNullOrWhiteSpace(contact.Phone))
        {
            throw new InvalidOperationException("Номер телефона недоступен из-за настроек приватности.");
        }
        return dialer.OpenAsync($"tel:{contact.Phone}");
    }
}
