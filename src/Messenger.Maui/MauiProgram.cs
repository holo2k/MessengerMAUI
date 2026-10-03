using Microsoft.Extensions.Logging;

using Messenger.Maui.Features.Auth;
using Messenger.Maui.Services;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Features.Contacts;
using Plugin.Maui.Audio;
using Messenger.Maui.Features.Music;
using Messenger.Maui.Features.Settings;

namespace Messenger.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.AddAudio()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var endpoints = ApiEndpointOptions.Create(
			DeviceInfo.Platform == DevicePlatform.Android,
#if DEBUG
			isDebug: true);
#else
			isDebug: false);
#endif
		builder.Services.AddSingleton(endpoints);
		builder.Services.AddSingleton<ISecureSessionStore, SecureSessionStore>();
		builder.Services.AddSingleton<INavigationService, NavigationService>();
		builder.Services.AddSingleton<IAuthApi>(_ => new ApiClient(new HttpClient
		{
			BaseAddress = endpoints.ApiBaseAddress
		}));
		builder.Services.AddSingleton<AuthSessionHandler>();
		builder.Services.AddSingleton(serviceProvider =>
		{
			var sessionHandler = serviceProvider.GetRequiredService<AuthSessionHandler>();
			sessionHandler.InnerHandler = new HttpClientHandler();
			return new HttpClient(sessionHandler) { BaseAddress = endpoints.ApiBaseAddress };
		});
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<RegistrationViewModel>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<RegistrationPage>();
		builder.Services.AddTransient<AppShell>();
		builder.Services.AddSingleton<IConversationApi, ConversationClient>();
		builder.Services.AddSingleton<Messenger.Maui.Services.IPhoneDialer, Messenger.Maui.Services.PhoneDialer>();
		builder.Services.AddSingleton<IChatRealtimeClient, ChatRealtimeClient>();
		builder.Services.AddSingleton<IMusicRealtimeClient>(services =>
			(ChatRealtimeClient)services.GetRequiredService<IChatRealtimeClient>());
		builder.Services.AddSingleton<IChatCursorStore, PreferencesChatCursorStore>();
		builder.Services.AddSingleton<IMediaCaptureService, NativeMediaCaptureService>();
		builder.Services.AddSingleton(services => new UploadClient(
			services.GetRequiredService<HttpClient>(), new HttpClient()));
		builder.Services.AddSingleton<IMediaUploader>(services => services.GetRequiredService<UploadClient>());
		builder.Services.AddSingleton<IAvatarClient>(services => services.GetRequiredService<UploadClient>());
		builder.Services.AddTransient<MediaComposerViewModel>();
		builder.Services.AddSingleton<IMusicApi, MusicApiClient>();
		builder.Services.AddSingleton<IAsyncDelay, AsyncDelay>();
		builder.Services.AddSingleton<IMusicDownloadService>(services => new MusicDownloadService(
			services.GetRequiredService<IMusicApi>(), new HttpClient()));
		builder.Services.AddSingleton<IAudioPlayerService>(services => new AudioPlayerService(
			services.GetRequiredService<IAudioManager>(), new HttpClient()));
		builder.Services.AddTransient<MusicViewModel>();
		builder.Services.AddTransient<MusicPage>();
		builder.Services.AddSingleton<ISettingsApi, SettingsApi>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<ChatSyncService>();
		builder.Services.AddTransient<ChatsViewModel>();
		builder.Services.AddTransient<ContactsViewModel>();
		builder.Services.AddTransient<ChatsPage>();
		builder.Services.AddTransient<ContactsPage>();

		return builder.Build();
	}
}
