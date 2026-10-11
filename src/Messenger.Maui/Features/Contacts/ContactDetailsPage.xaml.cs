using Messenger.Contracts.Contacts;

namespace Messenger.Maui.Features.Contacts;

public partial class ContactDetailsPage : ContentPage
{
    private ContactResponse _contact;
    private readonly ContactsViewModel _viewModel;
    private readonly Func<Task> _openChat;
    private bool _updating;
    private bool _editing;

    public ContactDetailsPage(ContactResponse contact, ContactsViewModel viewModel, Func<Task> openChat)
    {
        _contact = contact;
        _viewModel = viewModel;
        _openChat = openChat;
        InitializeComponent();
        BindingContext = contact;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (_editing) SetEditing(false);
        else await Navigation.PopAsync();
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_editing) { SetEditing(true); return; }
        await RunAsync(async () =>
        {
            var saved = await _viewModel.UpdateContactAsync(_contact, FirstNameEntry.Text, LastNameEntry.Text, MuteSwitch.IsToggled);
            _contact = _contact with { LocalFirstName = saved.LocalFirstName, LocalLastName = saved.LocalLastName, IsMuted = saved.IsMuted };
            BindingContext = _contact;
            SetEditing(false);
        });
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        InfoPanel.IsVisible = !editing;
        EditPanel.IsVisible = editing;
        HeaderTitle.Text = editing ? "Редактировать" : "Информация";
        BackButton.Text = editing ? "Закрыть" : "Назад";
        EditButton.Text = editing ? "Сохранить" : "Ред.";
        if (editing)
        {
            FirstNameEntry.Text = _contact.LocalFirstName ?? _contact.FirstName;
            LastNameEntry.Text = _contact.LocalLastName ?? _contact.LastName;
        }
    }

    private async void OnMuteToggled(object? sender, ToggledEventArgs e)
    {
        if (_updating || e.Value == _contact.IsMuted || _editing) return;
        await RunAsync(async () =>
        {
            _updating = true;
            try
            {
                await _viewModel.UpdateContactAsync(_contact, _contact.LocalFirstName, _contact.LocalLastName, e.Value);
                _contact = _contact with { IsMuted = e.Value };
                BindingContext = _contact;
            }
            finally { _updating = false; MuteSwitch.IsToggled = _contact.IsMuted; }
        });
    }

    private async void OnChatClicked(object? sender, EventArgs e) => await RunAsync(_openChat);
    private async void OnCallClicked(object? sender, EventArgs e) => await RunAsync(() => _viewModel.CallAsync(_contact));
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
