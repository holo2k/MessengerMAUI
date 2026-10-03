using Messenger.Contracts.Support;
using Messenger.Contracts.Users;
using Messenger.Maui.Features.Settings;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Settings;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Profile_privacy_session_two_factor_and_support_actions_are_forwarded()
    {
        var api = new FakeSettingsApi(); var vm = new SettingsViewModel(api);
        await vm.LoadAsync(); vm.FirstName = "Анна"; vm.Username = "anna"; vm.Bio = "bio";
        await vm.SaveProfileAsync(); await vm.SavePrivacyAsync(); await vm.RevokeSessionAsync(vm.Sessions.Single());
        await vm.BeginTwoFactorAsync("a@example.test"); await vm.ConfirmTwoFactorAsync("123456");
        await vm.CreateTicketAsync("Помощь", "Текст");
        Assert.Equal(1, api.ProfileUpdates); Assert.Equal(1, api.PrivacyUpdates); Assert.Equal(1, api.Revocations);
        Assert.Equal(1, api.TwoFactorConfirmations); Assert.Single(vm.Tickets);
    }

    [Fact]
    public async Task Deletion_warning_request_and_cancel_are_explicit()
    {
        var api = new FakeSettingsApi(); var vm = new SettingsViewModel(api);
        await vm.RequestDeletionAsync(true);
        Assert.True(vm.DeletionScheduled); Assert.Contains("30", vm.Status);
        await vm.CancelDeletionAsync();
        Assert.False(vm.DeletionScheduled); Assert.Equal(1, api.Cancellations);
    }

    private sealed class FakeSettingsApi : ISettingsApi
    {
        public int ProfileUpdates, PrivacyUpdates, Revocations, TwoFactorConfirmations, Cancellations;
        public Task<MeResponse> GetMeAsync(CancellationToken ct) => Task.FromResult(new MeResponse(Guid.NewGuid(), "Иван", "Иванов", null, null, "+79990000000", null, null));
        public Task UpdateMeAsync(UpdateMeRequest request, CancellationToken ct) { ProfileUpdates++; return Task.CompletedTask; }
        public Task<PrivacyResponse> GetPrivacyAsync(CancellationToken ct) => Task.FromResult(new PrivacyResponse("contacts", "everybody", "nobody"));
        public Task UpdatePrivacyAsync(PrivacyRequest request, CancellationToken ct) { PrivacyUpdates++; return Task.CompletedTask; }
        public Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionResponse>>([new(Guid.NewGuid(), "Android", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), true)]);
        public Task RevokeSessionAsync(Guid id, CancellationToken ct) { Revocations++; return Task.CompletedTask; }
        public Task<EmailChallengeResponse> BeginTwoFactorAsync(string email, CancellationToken ct) => Task.FromResult(new EmailChallengeResponse(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5)));
        public Task ConfirmTwoFactorAsync(Guid id, string code, CancellationToken ct) { TwoFactorConfirmations++; return Task.CompletedTask; }
        public Task DisableTwoFactorAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<EmailChallengeResponse> BeginPhoneChangeAsync(string phone, CancellationToken ct) => Task.FromResult(new EmailChallengeResponse(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5)));
        public Task ConfirmPhoneChangeAsync(Guid id, string code, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<SupportTicketResponse>> ListTicketsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SupportTicketResponse>>([]);
        public Task<SupportConversationResponse> CreateTicketAsync(string subject, string body, CancellationToken ct) => Task.FromResult(new SupportConversationResponse(new SupportTicketResponse(Guid.NewGuid(), Guid.NewGuid(), subject, "open", DateTimeOffset.UtcNow, null), []));
        public Task<AccountDeletionResponse> RequestDeletionAsync(CancellationToken ct) => Task.FromResult(new AccountDeletionResponse(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30)));
        public Task CancelDeletionAsync(CancellationToken ct) { Cancellations++; return Task.CompletedTask; }
    }
}
