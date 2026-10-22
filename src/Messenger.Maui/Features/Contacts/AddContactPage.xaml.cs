using Messenger.Contracts.Contacts;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Contacts;

public partial class AddContactPage : ContentPage
{
    private readonly AddContactViewModel _viewModel;
    private readonly CancellationTokenSource _searchLifetime = new();
    private bool _finished;
    private bool _isActive;

    public AddContactPage(IConversationApi api, UserSearchResponse? user = null)
    {
        InitializeComponent();
        BindingContext = _viewModel = new AddContactViewModel(api);
        if (user is not null)
        {
            _viewModel.Query = user.Username is { Length: > 0 } username ? "@" + username : user.Phone ?? string.Empty;
            _viewModel.SelectUser(user);
        }
    }

    protected override void OnAppearing() { base.OnAppearing(); _isActive = true; }
    protected override void OnDisappearing() { _isActive = false; _searchLifetime.Cancel(); base.OnDisappearing(); }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        if (_viewModel.IsBusy) return;
        QueryEntry.Unfocus();
        try { await SearchLoading.LoadAsync(() => _viewModel.SearchAsync(_searchLifetime.Token), replaceContent: true); }
        catch (OperationCanceledException) when (_searchLifetime.IsCancellationRequested) { }
        catch (Exception exception) { if (!_searchLifetime.IsCancellationRequested) await DisplayAlertAsync("Поиск", exception.Message, "OK"); }
    }

    private void OnUserSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not UserSearchResponse user) return;
        ResultsList.SelectedItem = null;
        _viewModel.SelectUser(user);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_finished || !_viewModel.CanSave) return;
        try
        {
            if (await _viewModel.SaveAsync() is null) return;
            _finished = true;
            if (_isActive) await Navigation.PopAsync();
        }
        catch (Exception exception) { if (_isActive) await DisplayAlertAsync("Не удалось добавить контакт", exception.Message, "OK"); }
    }

    private async void OnCancelClicked(object? sender, EventArgs e) { if (!_viewModel.IsSaving) await Navigation.PopAsync(); }
    protected override bool OnBackButtonPressed() => _viewModel.IsSaving || base.OnBackButtonPressed();
}
