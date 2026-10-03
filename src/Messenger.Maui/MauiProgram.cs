using Microsoft.Extensions.Logging;

using Messenger.Maui.Features.Auth;
using Messenger.Maui.Services;
using Messenger.Maui.Features.Chats;
using Messenger.Maui.Features.Contacts;
using Plugin.Maui.Audio;

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

		var apiBaseAddress = new Uri(
			DeviceInfo.Platform == DevicePlatform.Android
				? "https://10.0.2.2:7106/"
				: "https://localhost:7106/");
		builder.Services.AddSingleton<ISecureSessionStore, SecureSessionStore>();
		builder.Services.AddSingleton<INavigationService, NavigationService>();
		builder.Services.AddSingleton<IAuthApi>(_ => new ApiClient(new HttpClient
		{
			BaseAddress = apiBaseAddress
		}));
		builder.Services.AddSingleton<AuthSessionHandler>();
		builder.Services.AddSingleton(serviceProvider =>
		{
			var sessionHandler = serviceProvider.GetRequiredService<AuthSessionHandler>();
			sessionHandler.InnerHandler = new HttpClientHandler();
			return new HttpClient(sessionHandler) { BaseAddress = apiBaseAddress };
		});
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<RegistrationViewModel>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<RegistrationPage>();
		builder.Services.AddTransient<AppShell>();
		builder.Services.AddSingleton<IConversationApi, ConversationClient>();
		builder.Services.AddSingleton<Messenger.Maui.Services.IPhoneDialer, Messenger.Maui.Services.PhoneDialer>();
		builder.Services.AddSingleton<IChatRealtimeClient, ChatRealtimeClient>();
		builder.Services.AddSingleton<IChatCursorStore, PreferencesChatCursorStore>();
		builder.Services.AddSingleton<IMediaCaptureService, NativeMediaCaptureService>();
		builder.Services.AddSingleton(services => new UploadClient(
			services.GetRequiredService<HttpClient>(), new HttpClient()));
		builder.Services.AddSingleton<IMediaUploader>(services => services.GetRequiredService<UploadClient>());
		builder.Services.AddSingleton<IAvatarClient>(services => services.GetRequiredService<UploadClient>());
		builder.Services.AddTransient<MediaComposerViewModel>();
		builder.Services.AddTransient<ChatSyncService>();
		builder.Services.AddTransient<ChatsViewModel>();
		builder.Services.AddTransient<ContactsViewModel>();
		builder.Services.AddTransient<ChatsPage>();
		builder.Services.AddTransient<ContactsPage>();

		return builder.Build();
	}
}
