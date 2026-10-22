using Messenger.Maui.Services;
using Messenger.Maui.Features.Chats;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui.Features.Contacts;

public partial class ContactDetailsPage : ContentPage
{
    private readonly Guid _userId;
    private readonly UserDetailsViewModel _viewModel;
    private readonly Func<Task> _openChat;
    private bool _updating;
    private bool _editing;

    public ContactDetailsPage(Guid userId, UserDetailsViewModel viewModel, Func<Task> openChat)
    {
        _userId = userId;
        _viewModel = viewModel;
        _openChat = openChat;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ProfileLoading.LoadAsync(() => RunAsync(async () =>
        {
            await _viewModel.LoadAsync(_userId);
            MuteSwitch.IsToggled = _viewModel.IsMuted;
        }));
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
            await _viewModel.SaveAliasAsync(FirstNameEntry.Text, LastNameEntry.Text, MuteSwitch.IsToggled);
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
            FirstNameEntry.Text = _viewModel.Contact?.LocalFirstName ?? _viewModel.User?.FirstName;
            LastNameEntry.Text = _viewModel.Contact?.LocalLastName ?? _viewModel.User?.LastName;
        }
    }

    private async void OnMuteToggled(object? sender, ToggledEventArgs e)
    {
        if (_updating || e.Value == _viewModel.IsMuted || _editing || !_viewModel.CanEdit) return;
        await RunAsync(async () =>
        {
            _updating = true;
            try
            {
                await _viewModel.SaveAliasAsync(
                    _viewModel.Contact?.LocalFirstName,
                    _viewModel.Contact?.LocalLastName,
                    e.Value);
            }
            finally { _updating = false; MuteSwitch.IsToggled = _viewModel.IsMuted; }
        });
    }

    private async void OnChatClicked(object? sender, EventArgs e) => await RunAsync(_openChat);
    private async void OnAddContactClicked(object? sender, EventArgs e) => await RunAsync(async () =>
    {
        if (!_viewModel.CanAdd || _viewModel.User is not { } user) return;
        var api = Handler!.MauiContext!.Services.GetRequiredService<IConversationApi>();
        await Navigation.PushAsync(new AddContactPage(api, user));
    });
    private async void OnAttachmentsTapped(object? sender, TappedEventArgs e) => await RunAsync(async () =>
    {
        var api = Handler!.MauiContext!.Services.GetRequiredService<IConversationApi>();
        var chat = (await api.GetChatsAsync()).FirstOrDefault(value => value.Type == "direct" && value.Peer?.UserId == _userId);
        await Navigation.PushAsync(new SharedMediaPage(chat?.Id ?? Guid.Empty, api));
    });
    private async void OnCallClicked(object? sender, EventArgs e) => await RunAsync(_viewModel.CallAsync);
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { await DisplayAlertAsync("Ошибка", exception.Message, "OK"); }
    }
}
