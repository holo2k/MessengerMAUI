using CommunityToolkit.Mvvm.ComponentModel;

namespace Messenger.Maui.Services;

public sealed class ScreenLoadState : ObservableObject
{
    private int _visibleLoads;
    public bool HasLoaded { get; private set; }
    public bool IsLoading => !HasLoaded || _visibleLoads > 0;

    public IDisposable Begin(bool replaceContent = false)
    {
        var visible = !HasLoaded || replaceContent;
        if (visible) _visibleLoads++;
        OnPropertyChanged(nameof(IsLoading));
        return new LoadLease(this, visible);
    }

    private void Complete(bool visible)
    {
        if (visible) _visibleLoads--;
        HasLoaded = true;
        OnPropertyChanged(nameof(HasLoaded));
        OnPropertyChanged(nameof(IsLoading));
    }

    private sealed class LoadLease(ScreenLoadState owner, bool visible) : IDisposable
    {
        private ScreenLoadState? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Complete(visible);
    }
}
