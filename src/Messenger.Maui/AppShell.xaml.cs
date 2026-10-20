using Messenger.Maui.Controls;
using Messenger.Maui.Features.Music;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Maui;

public partial class AppShell : Shell
{
    private readonly MusicViewModel _music;
    private readonly Microsoft.Maui.Dispatching.IDispatcherTimer _playbackTimer;

    public AppShell(IServiceProvider services, MusicViewModel music)
    {
        InitializeComponent();
        _music = music;
        ChatsTab.ContentTemplate = new DataTemplate(() =>
        {
            var page = services.GetRequiredService<Features.Chats.ChatsPage>();
            var miniPlayer = new MiniPlayerView(_music, OpenMusicPlayerAsync);
            miniPlayer.SetCompact(true);
            miniPlayer.SetBinding(IsVisibleProperty, new Binding(nameof(MusicViewModel.HasCurrentTrack), source: _music));
            page.SetMiniPlayer(miniPlayer);
            return page;
        });
        ContactsTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Features.Contacts.ContactsPage>());
        MusicTab.ContentTemplate = new DataTemplate(() => ActivatorUtilities.CreateInstance<MusicPage>(services, _music));
        SettingsTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<Features.Settings.SettingsPage>());

        // The visual tab order must not determine the initial destination.
        MainTabs.CurrentItem = MainTabs.Items.First(section => section.Items.Contains(ChatsTab));
        CurrentItem = MainTabs;
        _playbackTimer = Dispatcher.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTimer.Tick += (_, _) =>
        {
            if (Navigation.ModalStack.LastOrDefault() is not MusicPlayerPage) _music.RefreshPlayback();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _music.RefreshPlayback();
        _playbackTimer.Start();
    }

    protected override void OnDisappearing()
    {
        _playbackTimer.Stop();
        base.OnDisappearing();
    }

    private Task OpenMusicPlayerAsync() => MusicPlayerPage.OpenAsync(Navigation, _music);
}
