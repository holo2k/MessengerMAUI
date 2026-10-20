using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Chats;

public sealed class GalleryTile(GalleryMediaItem item) : ObservableObject
{
    private int _selectionNumber;
    public GalleryMediaItem Item { get; } = item;
    public bool IsVideo => Item.Kind == MediaKind.Video;
    public string Duration => IsVideo ? PlaybackTime.Format(Item.DurationMs ?? 0) : "";
    public int SelectionNumber
    {
        get => _selectionNumber;
        set { if (SetProperty(ref _selectionNumber, value)) OnPropertyChanged(nameof(IsSelected)); }
    }
    public bool IsSelected => SelectionNumber > 0;
}

public sealed class AttachmentPickerViewModel(IGalleryService gallery, int selectionLimit) : ObservableObject
{
    private readonly List<GalleryTile> _selected = [];
    private string? _cursor;
    private bool _hasMore, _isLoading, _isPreparing;
    private string _status = "";
    private int _generation;
    private GalleryAccess _access = GalleryAccess.Denied;
    public ObservableCollection<GalleryTile> Items { get; } = [];
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(ShowEmpty)); } }
    public bool IsPreparing { get => _isPreparing; private set { if (SetProperty(ref _isPreparing, value)) NotifySelection(); } }
    public bool ShowEmpty => !IsLoading && Items.Count == 0;
    public bool HasLimitedAccess => _access == GalleryAccess.Limited;
    public bool NeedsPermission => _access == GalleryAccess.Denied;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public int SelectedCount => _selected.Count;
    public bool CanAttach => SelectedCount > 0 && !IsPreparing;
    public string AttachText => SelectedCount > 0 ? $"Прикрепить ({SelectedCount})" : "Прикрепить";

    public async Task OpenAsync(bool reselect = false, CancellationToken ct = default)
    {
        var generation = ++_generation;
        IsLoading = true;
        try
        {
            var access = await gallery.RequestAccessAsync(reselect, ct);
            if (generation != _generation) return;
            _access = access;
            OnPropertyChanged(nameof(HasLimitedAccess));
            OnPropertyChanged(nameof(NeedsPermission));
            _selected.Clear();
            Items.Clear();
            NotifySelection();
            OnPropertyChanged(nameof(ShowEmpty));
            _cursor = null;
            _hasMore = access != GalleryAccess.Denied;
            Status = access == GalleryAccess.Denied ? "Разрешите доступ к фото и видео, чтобы показать галерею." : "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) Status = exception.Message; }
        finally { if (generation == _generation) IsLoading = false; }
        if (generation == _generation && !ct.IsCancellationRequested) await LoadMoreAsync(ct);
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (IsLoading || !_hasMore) return;
        var generation = _generation;
        IsLoading = true;
        try
        {
            var page = await gallery.GetPageAsync(_cursor, 60, ct);
            if (generation != _generation || ct.IsCancellationRequested) return;
            var known = Items.Select(tile => tile.Item.Id).ToHashSet();
            foreach (var item in page.Items)
                if (known.Add(item.Id)) Items.Add(new(item));
            _hasMore = page.NextCursor is not null && page.NextCursor != _cursor;
            _cursor = page.NextCursor;
            OnPropertyChanged(nameof(ShowEmpty));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { if (generation == _generation) Status = $"Не удалось открыть галерею: {exception.Message}"; }
        finally { if (generation == _generation) IsLoading = false; }
    }

    public void Toggle(GalleryTile tile)
    {
        if (IsPreparing || !Items.Contains(tile)) return;
        if (_selected.Remove(tile)) tile.SelectionNumber = 0;
        else
        {
            if (_selected.Count >= selectionLimit)
            {
                Status = selectionLimit > 0 ? $"Можно выбрать ещё {selectionLimit} вложений." : "В сообщении уже 10 вложений.";
                return;
            }
            _selected.Add(tile);
        }
        for (var index = 0; index < _selected.Count; index++) _selected[index].SelectionNumber = index + 1;
        Status = "";
        NotifySelection();
    }

    public async Task<IReadOnlyList<MediaSelection>> PrepareSelectionAsync(CancellationToken ct = default)
    {
        if (!CanAttach) return [];
        IsPreparing = true;
        try
        {
            var result = new List<MediaSelection>();
            foreach (var tile in _selected.ToArray()) result.Add(await gallery.CreateSelectionAsync(tile.Item, ct));
            return result;
        }
        finally { IsPreparing = false; }
    }

    public void Close() { _generation++; IsLoading = false; }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanAttach));
        OnPropertyChanged(nameof(AttachText));
    }
}
