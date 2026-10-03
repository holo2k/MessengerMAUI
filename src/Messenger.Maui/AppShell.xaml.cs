namespace Messenger.Maui;

public partial class AppShell : Shell
{
	public AppShell(IServiceProvider services)
	{
		InitializeComponent();
		ChatsTab.ContentTemplate = new DataTemplate(() =>
			Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
				.GetRequiredService<Features.Chats.ChatsPage>(services));
		ContactsTab.ContentTemplate = new DataTemplate(() =>
			Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
				.GetRequiredService<Features.Contacts.ContactsPage>(services));
	}
}
