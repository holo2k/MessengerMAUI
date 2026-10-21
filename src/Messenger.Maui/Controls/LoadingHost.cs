using Messenger.Maui.Services;

namespace Messenger.Maui.Controls;

[ContentProperty(nameof(LoadedContent))]
public sealed class LoadingHost : ContentView
{
    public static readonly BindableProperty LoadedContentProperty = BindableProperty.Create(nameof(LoadedContent), typeof(View), typeof(LoadingHost), null,
        propertyChanged: (view, _, value) => ((LoadingHost)view)._content.Content = (View?)value);
    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(SkeletonKind), typeof(LoadingHost), SkeletonKind.List,
        propertyChanged: (view, _, value) => ((LoadingHost)view)._skeleton.Kind = (SkeletonKind)value);
    private readonly ContentView _content = new() { IsVisible = false };
    private readonly SkeletonView _skeleton = new();
    private readonly ScreenLoadState _state = new();
    public View? LoadedContent { get => (View?)GetValue(LoadedContentProperty); set => SetValue(LoadedContentProperty, value); }
    public SkeletonKind Kind { get => (SkeletonKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public LoadingHost()
    {
        var grid = new Grid();
        grid.Add(_content);
        grid.Add(_skeleton);
        Content = grid;
        _state.PropertyChanged += (_, _) =>
        {
            _content.IsVisible = !_state.IsLoading;
            _skeleton.IsVisible = _state.IsLoading;
        };
        SemanticProperties.SetDescription(_skeleton, "Загрузка");
    }

    public async Task LoadAsync(Func<Task> action, bool replaceContent = false)
    {
        using var loading = _state.Begin(replaceContent);
        await action();
    }
}
