using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Services;

public sealed class ScreenLoadStateTests
{
    [Fact]
    public void Initial_loading_hides_empty_content_until_all_overlapping_requests_finish()
    {
        var state = new ScreenLoadState();
        Assert.True(state.IsLoading);
        using var first = state.Begin();
        using var second = state.Begin();
        first.Dispose();
        Assert.True(state.IsLoading);
        second.Dispose();
        Assert.True(state.HasLoaded);
        Assert.False(state.IsLoading);
    }

    [Fact]
    public void Refresh_keeps_loaded_content_and_a_new_search_can_show_a_skeleton()
    {
        var state = new ScreenLoadState();
        state.Begin().Dispose();
        using var refresh = state.Begin();
        Assert.False(state.IsLoading);
        using var search = state.Begin(replaceContent: true);
        Assert.True(state.IsLoading);
        refresh.Dispose();
        Assert.True(state.IsLoading);
        search.Dispose();
        Assert.False(state.IsLoading);
    }

    [Fact]
    public async Task Failed_requests_release_the_skeleton_and_allow_another_load()
    {
        var state = new ScreenLoadState();
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            using var loading = state.Begin();
            await Task.Yield();
            throw new HttpRequestException("offline");
        });
        Assert.False(state.IsLoading);
        using var retry = state.Begin(replaceContent: true);
        Assert.True(state.IsLoading);
        retry.Dispose();
        Assert.False(state.IsLoading);
    }
}
