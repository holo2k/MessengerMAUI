using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public enum AttachmentPickerAction { File, Music }

public partial class AttachmentPickerView : ContentView
{
    private readonly AttachmentPickerViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _refreshOnResume;
    public event Action? CloseRequested;
    public event Func<IReadOnlyList<MediaSelection>, Task>? AttachRequested;
    public event Func<AttachmentPickerAction, Task>? ActionRequested;

    public AttachmentPickerView(IGalleryService gallery, int selectionLimit)
    {
        InitializeComponent();
        BindingContext = _viewModel = new(gallery, selectionLimit);
    }

    public Task OpenAsync() => _viewModel.OpenAsync(ct: _lifetime.Token);
    private void OnGallerySizeChanged(object? sender, EventArgs e) => _viewModel?.UpdateGalleryWidth(GalleryList.Width);
    public async Task RefreshOnResumeAsync()
    {
        if (!_refreshOnResume || _lifetime.IsCancellationRequested) return;
        _refreshOnResume = false;
        await OpenAsync();
    }
    public void Close() { _lifetime.Cancel(); _viewModel.Close(); }
    private void OnCloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke();
    private void OnTileTapped(object? sender, TappedEventArgs e) { if (e.Parameter is GalleryTile tile) _viewModel.Toggle(tile); }
    private async void OnLoadMore(object? sender, EventArgs e) => await _viewModel.LoadMoreAsync(_lifetime.Token);
    private void OnSheetSwipedDown(object? sender, SwipedEventArgs e) => CloseRequested?.Invoke();
    private async void OnGalleryClicked(object? sender, EventArgs e)
    {
        _refreshOnResume = DeviceInfo.Platform == DevicePlatform.iOS;
        await _viewModel.OpenAsync(reselect: true, ct: _lifetime.Token);
    }
    private async void OnGalleryTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.Items.Count > 0) GalleryList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
        else await OpenAsync();
    }
    private void OnPermissionClicked(object? sender, EventArgs e) { _refreshOnResume = true; AppInfo.Current.ShowSettingsUI(); }
    private async void OnFileTapped(object? sender, TappedEventArgs e) { if (ActionRequested is not null) await ActionRequested(AttachmentPickerAction.File); }
    private async void OnMusicTapped(object? sender, TappedEventArgs e) { if (ActionRequested is not null) await ActionRequested(AttachmentPickerAction.Music); }
    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        try
        {
            var items = await _viewModel.PrepareSelectionAsync(_lifetime.Token);
            if (items.Count > 0 && AttachRequested is not null) await AttachRequested(items);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { await Shell.Current.DisplayAlertAsync("Вложение", exception.Message, "OK"); }
    }
}
