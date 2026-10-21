using Messenger.Maui.Features.Chats;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Media;

public sealed class AttachmentPickerTests
{
    [Fact]
    public async Task Gallery_cells_keep_explicit_square_dimensions_on_initial_load_paging_and_resize()
    {
        var gallery = new FakeGallery();
        gallery.Pages.Enqueue(new([Item("first")], "next"));
        gallery.Pages.Enqueue(new([Item("second")], null));
        var viewModel = new AttachmentPickerViewModel(gallery, 10);
        viewModel.UpdateGalleryWidth(394);
        await viewModel.OpenAsync();
        Assert.Equal(130, Assert.Single(viewModel.Items).TileSize);
        viewModel.UpdateGalleryWidth(304);
        await viewModel.LoadMoreAsync();
        Assert.All(viewModel.Items, item => Assert.Equal(100, item.TileSize));
        viewModel.UpdateGalleryWidth(-1);
        viewModel.UpdateGalleryWidth(double.NaN);
        Assert.All(viewModel.Items, item => Assert.Equal(100, item.TileSize));
    }

    [Fact]
    public async Task Permission_denial_never_queries_or_opens_gallery_files()
    {
        var gallery = new FakeGallery { Access = GalleryAccess.Denied };
        var viewModel = new AttachmentPickerViewModel(gallery, 10);
        await viewModel.OpenAsync();
        await viewModel.LoadMoreAsync();
        Assert.True(viewModel.NeedsPermission);
        Assert.True(viewModel.ShowEmpty);
        Assert.Empty(viewModel.Items);
        Assert.Equal(0, gallery.PageCalls);
        Assert.Empty(await viewModel.PrepareSelectionAsync());
        Assert.Empty(gallery.Prepared);
    }

    [Fact]
    public async Task Limited_gallery_pages_without_duplicates_and_only_prepares_selected_files_in_selection_order()
    {
        var gallery = new FakeGallery { Access = GalleryAccess.Limited };
        gallery.Pages.Enqueue(new([Item("first"), Item("video", MediaKind.Video)], "next"));
        gallery.Pages.Enqueue(new([Item("video", MediaKind.Video), Item("last")], null));
        var viewModel = new AttachmentPickerViewModel(gallery, 3);
        await viewModel.OpenAsync();
        viewModel.Toggle(viewModel.Items[1]);
        await viewModel.LoadMoreAsync();
        viewModel.Toggle(viewModel.Items[2]);
        viewModel.Toggle(viewModel.Items[0]);
        Assert.Empty(gallery.Prepared);
        Assert.True(viewModel.HasLimitedAccess);
        Assert.Equal([1, 2, 3], new[] { viewModel.Items[1].SelectionNumber, viewModel.Items[2].SelectionNumber, viewModel.Items[0].SelectionNumber });

        var selections = await viewModel.PrepareSelectionAsync();
        await viewModel.LoadMoreAsync();

        Assert.Equal(["video", "last", "first"], gallery.Prepared);
        Assert.Equal(MediaKind.Video, selections[0].Kind);
        Assert.Equal(4_200, selections[0].DurationMs);
        Assert.Equal(2, gallery.PageCalls);
        Assert.Equal(3, viewModel.Items.Count);
    }

    [Fact]
    public async Task Selection_limit_accounts_for_existing_attachments_and_deselection_renumbers_the_remaining_items()
    {
        var gallery = new FakeGallery();
        gallery.Pages.Enqueue(new([Item("a"), Item("b"), Item("c")], null));
        var viewModel = new AttachmentPickerViewModel(gallery, 2);
        await viewModel.OpenAsync();
        viewModel.Toggle(viewModel.Items[0]);
        viewModel.Toggle(viewModel.Items[1]);
        viewModel.Toggle(viewModel.Items[2]);
        Assert.Equal(2, viewModel.SelectedCount);
        Assert.False(viewModel.Items[2].IsSelected);
        viewModel.Toggle(viewModel.Items[0]);
        Assert.Equal(1, viewModel.Items[1].SelectionNumber);
        viewModel.Toggle(viewModel.Items[2]);
        Assert.Equal(["b", "c"], (await viewModel.PrepareSelectionAsync()).Select(item => item.FileName));
    }

    [Fact]
    public async Task Closing_the_picker_ignores_a_late_gallery_response()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<GalleryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gallery = new FakeGallery { LoadPage = () => { started.SetResult(); return pending.Task; } };
        var viewModel = new AttachmentPickerViewModel(gallery, 10);
        var load = viewModel.OpenAsync();
        await started.Task;
        viewModel.Close();
        pending.SetResult(new([Item("late")], null));
        await load;
        Assert.Empty(viewModel.Items);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task Reselecting_permission_discards_inaccessible_selections_and_reload_starts_at_the_first_page()
    {
        var gallery = new FakeGallery();
        gallery.Pages.Enqueue(new([Item("old")], "old-cursor"));
        var viewModel = new AttachmentPickerViewModel(gallery, 10);
        await viewModel.OpenAsync();
        viewModel.Toggle(viewModel.Items[0]);
        gallery.Pages.Enqueue(new([Item("new")], null));
        await viewModel.OpenAsync(reselect: true);
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.Equal("new", Assert.Single(viewModel.Items).Item.Id);
        Assert.Equal([null, null], gallery.Cursors);
        Assert.True(gallery.Reselected);
    }

    private static GalleryMediaItem Item(string id, MediaKind kind = MediaKind.Photo) =>
        new(id, id, kind, kind == MediaKind.Video ? "video/mp4" : "image/jpeg", 3, kind == MediaKind.Video ? 4_200 : null);

    private sealed class FakeGallery : IGalleryService
    {
        public GalleryAccess Access { get; set; } = GalleryAccess.Granted;
        public Queue<GalleryPage> Pages { get; } = [];
        public List<string> Prepared { get; } = [];
        public List<string?> Cursors { get; } = [];
        public Func<Task<GalleryPage>>? LoadPage { get; init; }
        public int PageCalls { get; private set; }
        public bool Reselected { get; private set; }
        public Task<GalleryAccess> RequestAccessAsync(bool reselect = false, CancellationToken ct = default) { Reselected = reselect; return Task.FromResult(Access); }
        public Task<GalleryPage> GetPageAsync(string? cursor, int pageSize, CancellationToken ct = default)
        {
            PageCalls++;
            Cursors.Add(cursor);
            return LoadPage?.Invoke() ?? Task.FromResult(Pages.Dequeue());
        }
        public Task<byte[]?> GetThumbnailAsync(GalleryMediaItem item, int size, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MediaSelection> CreateSelectionAsync(GalleryMediaItem item, CancellationToken ct = default)
        {
            Prepared.Add(item.Id);
            return Task.FromResult(new MediaSelection(item.Kind, item.FileName, item.ContentType, item.Size, item.DurationMs,
                () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]))));
        }
    }
}
