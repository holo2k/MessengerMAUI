# Messenger MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a runnable Android/iOS .NET MAUI messenger client and modular ASP.NET Core backend that demonstrate the complete approved MVP behavior.

**Architecture:** A feature-oriented modular monolith keeps identity, users, contacts, chats, media, music, settings, and support isolated behind application interfaces while sharing PostgreSQL and MinIO. MAUI consumes only versioned contracts through REST and SignalR; application services remain testable without UI or infrastructure.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, Npgsql 10.0.3, PostgreSQL, SignalR, MinIO 7.0.0, .NET MAUI, CommunityToolkit.Mvvm 8.4.2, JWT, MailKit, xUnit, Testcontainers 4.15.0, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-10-02-messenger-mvp-design.md`

## Global Constraints

- Target .NET 10 and Android/iOS MAUI; final iOS build/signing requires a Mac with Xcode.
- Use one ASP.NET Core modular-monolith deployment with PostgreSQL and MinIO.
- Production transport is HTTPS/WSS only; cleartext is permitted only by a narrowly scoped Debug emulator configuration.
- No E2E, FCM, APNs, VoIP, or real SMS implementation in this MVP.
- Development SMS code is exactly `111111`; a missing production SMS provider must prevent production startup.
- Access tokens last 15 minutes; rotating refresh sessions last 30 days.
- A sender can globally delete a message for 48 hours; account deletion has a 30-day grace period.
- Message bodies and phone numbers use AES-256-GCM; blind indexes use a separate HMAC key.
- No secret, raw token, verification code, message body, or raw phone number may enter source control or logs.
- SMTP uses `smtp.mail.ru:465` with SSL/TLS and a secret supplied outside Git.
- SMTP sender is `messengernotifies@mail.ru`; its application password is never committed.
- SignalR accelerates delivery but REST reconciliation remains the source of truth.
- Use TDD: observe every behavior test failing for the expected reason before adding its production behavior.

## Review Focus

- Concurrent refresh/reuse: only one rotation succeeds; replay revokes the whole token family (Task 3 test).
- Cross-user object guessing: inaccessible chats, messages, files, tracks, and tickets disclose no data (Tasks 6, 7, 9, and 12 tests).
- Missed SignalR events: reconnect reconciles every resource after the saved cursor without duplicates (Task 8 test).
- Spoofed/oversized upload: mismatched magic bytes, MIME, checksum, or size never creates a usable object (Task 9 test).
- Track blocked during playback/download: new stream/download authorization fails and the client stops with an actionable state (Tasks 10 and 11 tests).

## Canonical cross-task interfaces

These signatures are the source of truth for task boundaries. Feature tasks may add private helpers but must not rename or duplicate these contracts.

```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }
public readonly record struct EncryptedValue(string Ciphertext, string Nonce, string Tag, int KeyVersion);
public interface IFieldCipher {
    EncryptedValue Encrypt(string plaintext);
    string Decrypt(EncryptedValue value);
}
public interface IBlindIndex { string Compute(string normalizedValue); }

public interface IAuthService {
    Task RequestChallengeAsync(RequestLoginChallengeCommand command, CancellationToken ct);
    Task<AuthTokens> RegisterAsync(RegisterCommand command, CancellationToken ct);
    Task<LoginResult> LoginAsync(LoginCommand command, CancellationToken ct);
    Task<AuthTokens> ConfirmTwoFactorAsync(ConfirmTwoFactorCommand command, CancellationToken ct);
    Task<AuthTokens> RefreshAsync(RefreshCommand command, CancellationToken ct);
    Task LogoutAsync(Guid sessionId, Guid userId, CancellationToken ct);
    Task LogoutAllAsync(Guid userId, CancellationToken ct);
}
public interface IEmailSender {
    Task SendVerificationCodeAsync(string email, string code, CancellationToken ct);
}
public interface IChatService {
    Task<ChatDto> CreateDirectAsync(Guid actorId, Guid peerId, CancellationToken ct);
    Task<ChatDto> CreateGroupAsync(Guid actorId, CreateGroupCommand command, CancellationToken ct);
    Task UpdateMemberAsync(Guid actorId, Guid chatId, Guid memberId, UpdateMemberCommand command, CancellationToken ct);
}
public interface IMessageService {
    Task<MessageDto> SendAsync(Guid actorId, Guid chatId, SendMessageCommand command, CancellationToken ct);
    Task<MessagePageDto> ListAsync(Guid actorId, Guid chatId, string? cursor, int limit, CancellationToken ct);
    Task<MessagePageDto> SearchAsync(Guid actorId, Guid chatId, string query, string? cursor, int limit, CancellationToken ct);
    Task MarkReadAsync(Guid actorId, Guid chatId, long sequence, CancellationToken ct);
}
public interface IUploadService {
    Task<UploadSessionDto> CreateAsync(Guid actorId, CreateUploadCommand command, CancellationToken ct);
    Task<StoredObjectDto> CompleteAsync(Guid actorId, Guid uploadId, CompleteUploadCommand command, CancellationToken ct);
    Task<DownloadAuthorizationDto> AuthorizeDownloadAsync(Guid actorId, Guid objectId, CancellationToken ct);
}
public interface IMusicService {
    Task<MusicPageDto> SearchAsync(Guid actorId, string query, string? cursor, int limit, CancellationToken ct);
    Task AddToLibraryAsync(Guid actorId, Guid trackId, CancellationToken ct);
    Task RemoveFromLibraryAsync(Guid actorId, Guid trackId, CancellationToken ct);
    Task<DownloadAuthorizationDto> AuthorizeStreamAsync(Guid actorId, Guid trackId, CancellationToken ct);
}
public interface IChatRealtimeClient {
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    IAsyncEnumerable<ChatEventDto> ReadEventsAsync(CancellationToken ct);
}
```

---

## Milestone 1: Foundation, identity, profile, and session shell

### Task 1: Solution skeleton and domain primitives

**Files:**
- Create: `Messenger.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`
- Create: `src/Messenger.Domain/Messenger.Domain.csproj`
- Create: `src/Messenger.Application/Messenger.Application.csproj`
- Create: `src/Messenger.Infrastructure/Messenger.Infrastructure.csproj`
- Create: `src/Messenger.Api/Messenger.Api.csproj`
- Create: `src/Messenger.Contracts/Messenger.Contracts.csproj`
- Create: `src/Messenger.Maui/Messenger.Maui.csproj` and generated Android/iOS MAUI shell files
- Create: `tests/Messenger.Domain.Tests/Messenger.Domain.Tests.csproj`
- Create: remaining test project shells under `tests/`
- Create: `src/Messenger.Domain/Common/Entity.cs`, `UtcInstant.cs`, `PhoneNumber.cs`
- Test: `tests/Messenger.Domain.Tests/Common/PhoneNumberTests.cs`

**Interfaces:**
- Produces: `PhoneNumber.Parse(string raw, string defaultRegion): PhoneNumber`, `PhoneNumber.E164: string`, `IClock.UtcNow: DateTimeOffset`.
- Consumes: none.

- [ ] **Step 1: Install the .NET 10 MAUI workload if absent, then create only mechanical project scaffolding, references, central package management, and test runners; add no product behavior.**
- [ ] **Step 2: Write failing tests** proving `+79991234567` is preserved, `8 (999) 123-45-67` normalizes with the supplied `RU` country context, and malformed/empty input is rejected.
- [ ] **Step 3: Run `dotnet test tests/Messenger.Domain.Tests --filter FullyQualifiedName~PhoneNumberTests` and confirm failure because `PhoneNumber` behavior is absent.**
- [ ] **Step 4: Implement the minimal domain primitives and run the focused test, then `dotnet test Messenger.slnx`; expect all green.**
- [ ] **Step 5: Commit with `git commit -m "build: scaffold messenger solution"`.**

### Task 2: Persistence, migrations, containers, and cryptography

**Files:**
- Create: `src/Messenger.Infrastructure/Persistence/MessengerDbContext.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Configurations/*.cs`
- Create: `src/Messenger.Application/Security/IFieldCipher.cs`, `IBlindIndex.cs`
- Create: `src/Messenger.Infrastructure/Security/AesGcmFieldCipher.cs`, `HmacBlindIndex.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/00000000000000_Foundation.cs`
- Create: `docker-compose.yml`, `.env.example`, `.gitignore`
- Test: `tests/Messenger.Infrastructure.Tests/Security/FieldCipherTests.cs`
- Test: `tests/Messenger.Infrastructure.Tests/Persistence/SchemaTests.cs`

**Interfaces:**
- Produces: `IFieldCipher.Encrypt(string): EncryptedValue`, `IFieldCipher.Decrypt(EncryptedValue): string`, `IBlindIndex.Compute(string): string`, `MessengerDbContext`.
- Consumes: Task 1 primitives.

- [ ] **Step 1: Write failing cipher tests** for round trip, different nonces for identical plaintext, wrong-key/tampered-tag rejection, and key-version preservation.
- [ ] **Step 2: Run the cipher test filter and confirm it fails because implementations are missing.**
- [ ] **Step 3: Implement AES-256-GCM and HMAC-SHA-256 providers with options validation; rerun focused tests.**
- [ ] **Step 4: Write a failing Testcontainers foundation test** asserting migrations apply to an empty PostgreSQL database, EF migration history is created, and a second application is idempotent.
- [ ] **Step 5: Add DbContext, a foundation migration with no feature tables, PostgreSQL/MinIO Compose services and health checks; run `docker compose config`, schema tests, then the full suite. Feature tasks own their tables and constraints.**
- [ ] **Step 6: Commit with `git commit -m "feat: add persistence and encryption foundation"`.**

### Task 3: Registration, login, refresh rotation, and development SMS

**Files:**
- Create: `src/Messenger.Domain/Identity/User.cs`, `LoginChallenge.cs`, `RefreshSession.cs`
- Create: `src/Messenger.Application/Identity/IAuthService.cs`, `ISmsSender.cs`, request/result records
- Create: `src/Messenger.Application/Identity/AuthService.cs`
- Create: `src/Messenger.Infrastructure/Identity/DevelopmentSmsSender.cs`, `JwtTokenIssuer.cs`, `RefreshTokenGenerator.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_Identity.cs`
- Create: `src/Messenger.Api/Endpoints/AuthEndpoints.cs`
- Create: `src/Messenger.Contracts/Auth/*.cs`
- Test: `tests/Messenger.Application.Tests/Identity/AuthServiceTests.cs`
- Test: `tests/Messenger.Api.IntegrationTests/Auth/AuthEndpointTests.cs`

**Interfaces:**
- Produces: `IAuthService.RequestChallengeAsync`, `RegisterAsync`, `LoginAsync`, `RefreshAsync`, `LogoutAsync`, `LogoutAllAsync`; `/api/auth/*` contracts.
- Consumes: `PhoneNumber`, `IClock`, `MessengerDbContext`, `IBlindIndex`.

- [ ] **Step 1: Write failing application tests** for challenge expiry/attempt limits/single use, fixed development code `111111`, duplicate phone rejection, 15-minute access expiry, and 30-day refresh expiry.
- [ ] **Step 2: Write the Review Focus failing concurrency test:** two rotations of the same refresh token yield exactly one success; replay revokes the token family and its replacement.
- [ ] **Step 3: Run the identity tests and confirm expected missing-service failures.**
- [ ] **Step 4: Implement domain entities and auth service minimally; rerun focused tests.**
- [ ] **Step 5: Write failing HTTP integration tests** for all auth routes, unique phone blind-index enforcement, RFC Problem Details, rate-limit responses, and production startup rejection when only `DevelopmentSmsSender` is configured.
- [ ] **Step 6: Map endpoints, JWT authentication, rate limits, and options validation; run integration tests and the full suite.**
- [ ] **Step 7: Commit with `git commit -m "feat: add phone authentication and rotating sessions"`.**

### Task 4: Profile, privacy, active sessions, and email 2FA

**Files:**
- Create: `src/Messenger.Domain/Users/UserProfile.cs`, `UserPrivacySettings.cs`, `UserSecuritySettings.cs`
- Create: `src/Messenger.Application/Users/IProfileService.cs`, `ITwoFactorService.cs`, `IEmailSender.cs`
- Create: `src/Messenger.Infrastructure/Email/MailKitEmailSender.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_ProfilesAndTwoFactor.cs`
- Create: `src/Messenger.Api/Endpoints/ProfileEndpoints.cs`
- Create: `src/Messenger.Contracts/Users/*.cs`
- Test: `tests/Messenger.Application.Tests/Users/ProfileAndTwoFactorTests.cs`
- Test: `tests/Messenger.Api.IntegrationTests/Users/ProfileEndpointTests.cs`

**Interfaces:**
- Produces: profile/privacy/session/phone-change methods, `ITwoFactorService.BeginEmailSetupAsync`, `ConfirmEmailAsync`, `ConfirmLoginAsync`, `DisableAsync`; `/api/me/*`, `/api/auth/2fa/confirm`.
- Consumes: Task 3 auth sessions and challenge infrastructure.

- [ ] **Step 1: Write failing tests** for username uniqueness/case folding, privacy filtering, session revocation, recent challenge required for phone change, single-use email code, and login pending-token flow.
- [ ] **Step 2: Run focused tests and confirm missing behavior.**
- [ ] **Step 3: Implement profile/privacy/2FA services and MailKit adapter with `smtp.mail.ru:465` SSL/TLS options; never load a real secret in tests.**
- [ ] **Step 4: Write and run endpoint integration tests** including SMTP failure leaving 2FA disabled and unauthorized session deletion returning `404`.
- [ ] **Step 5: Run the full suite and commit with `git commit -m "feat: add profile privacy and email two-factor auth"`.**

### Task 5: MAUI authentication shell and resilient token client

**Files:**
- Create: `src/Messenger.Maui/AppShell.xaml`, `AppShell.xaml.cs`
- Create: `src/Messenger.Maui/Features/Auth/LoginPage.xaml`, `LoginViewModel.cs`, `RegistrationPage.xaml`, `RegistrationViewModel.cs`
- Create: `src/Messenger.Maui/Services/ApiClient.cs`, `AuthSessionHandler.cs`, `ISecureSessionStore.cs`, `SecureSessionStore.cs`
- Create: `src/Messenger.Maui/Services/NavigationService.cs`
- Test: `tests/Messenger.Maui.Tests/Auth/AuthViewModelTests.cs`
- Test: `tests/Messenger.Maui.Tests/Services/AuthSessionHandlerTests.cs`

**Interfaces:**
- Produces: `IAuthApi`, `ISecureSessionStore`, authenticated `HttpClient`, four-tab authenticated Shell.
- Consumes: Task 3/4 contracts.

- [ ] **Step 1: Write failing view-model tests** for country-code/phone composition, code request, registration, restored session, and login navigation.
- [ ] **Step 2: Write failing handler tests** proving concurrent `401` responses cause one refresh, retry once, persist rotated tokens, and clear/navigate on refresh failure.
- [ ] **Step 3: Run the MAUI tests and confirm missing types/behavior.**
- [ ] **Step 4: Implement minimal view models, services, SecureStorage adapter, and plain XAML screens; rerun tests.**
- [ ] **Step 5: Build `Messenger.Maui` for Android, run all tests, and commit with `git commit -m "feat: add MAUI authentication shell"`.**

---

## Milestone 2: Contacts, chats, messages, and real time

### Task 6: Contacts, chat membership, groups, and folders

**Files:**
- Create: `src/Messenger.Domain/Contacts/Contact.cs`
- Create: `src/Messenger.Domain/Chats/Chat.cs`, `ChatMember.cs`, `ChatFolder.cs`
- Create: `src/Messenger.Application/Contacts/ContactService.cs`, `IContactService.cs`
- Create: `src/Messenger.Application/Chats/ChatService.cs`, `IChatService.cs`, `ChatFolderService.cs`
- Create: `src/Messenger.Api/Endpoints/ContactEndpoints.cs`, `ChatEndpoints.cs`, `ChatFolderEndpoints.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_ContactsChatsAndFolders.cs`
- Create: `src/Messenger.Contracts/Contacts/*.cs`, `Chats/*.cs`
- Test: `tests/Messenger.Domain.Tests/Chats/ChatMembershipTests.cs`
- Test: `tests/Messenger.Api.IntegrationTests/Chats/ChatAndContactEndpointTests.cs`

**Interfaces:**
- Produces: contact discovery/CRUD, direct/group CRUD, member role transitions, archive/mute/read/hide, folder CRUD/reorder.
- Consumes: Task 4 privacy-filtered profiles.

- [ ] **Step 1: Write failing domain/application tests** for self/duplicate contacts, local aliases, one direct chat per canonical pair, owner/admin/member permissions, last-owner protection, ordered folders, and hidden-chat reactivation.
- [ ] **Step 2: Run tests and confirm expected failures.**
- [ ] **Step 3: Implement entities/services and rerun focused tests.**
- [ ] **Step 4: Write endpoint integration tests**, including unique direct-pair and case-insensitive username constraints, Review Focus cross-user IDs returning `404`, stable cursor pagination, exact phone/username discovery, and privacy-filtered responses.
- [ ] **Step 5: Map endpoints/policies, run the full suite, and commit with `git commit -m "feat: add contacts chats groups and folders"`.**

### Task 7: Messages, read state, deletion, pins, and encrypted search

**Files:**
- Create: `src/Messenger.Domain/Chats/Message.cs`, `HiddenMessage.cs`, `PinnedMessage.cs`
- Create: `src/Messenger.Application/Chats/IMessageService.cs`, `MessageService.cs`, `IMessageSearchService.cs`
- Create: `src/Messenger.Api/Endpoints/MessageEndpoints.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_Messages.cs`
- Create: `src/Messenger.Contracts/Messages/*.cs`
- Test: `tests/Messenger.Domain.Tests/Chats/MessageRulesTests.cs`
- Test: `tests/Messenger.Application.Tests/Chats/MessageServiceTests.cs`
- Test: `tests/Messenger.Api.IntegrationTests/Chats/MessageEndpointTests.cs`

**Interfaces:**
- Produces: send/list/edit/delete/hide/read/pin/search/media-list commands and queries; ordered `MessageSequence` and reconciliation cursor.
- Consumes: Task 2 cipher/index and Task 6 membership policies.

- [ ] **Step 1: Write failing tests** for idempotent `(sender, clientMessageId)`, monotonic per-chat sequence, membership enforcement, edit ownership, 48-hour global deletion, per-user hiding, read positions, and pin permissions.
- [ ] **Step 2: Write failing blind-search tests** proving complete-word matches work, unrelated words do not, ciphertext never appears in query results/logs, and tampering fails closed.
- [ ] **Step 3: Run focused tests, implement minimal message/search services, and rerun them.**
- [ ] **Step 4: Write HTTP integration tests** for the `(sender_id, client_message_id)` unique constraint, cursor pagination, and Review Focus cross-user message/search/media access returning `404` without disclosure.
- [ ] **Step 5: Map endpoints, run the full suite, and commit with `git commit -m "feat: add encrypted messaging and search"`.**

### Task 8: SignalR delivery, reconciliation, and MAUI chat/contact screens

**Files:**
- Create: `src/Messenger.Api/Hubs/ChatHub.cs`, `IChatEventPublisher.cs`, `SignalRChatEventPublisher.cs`
- Create: `src/Messenger.Contracts/Realtime/*.cs`
- Create: `src/Messenger.Maui/Services/ChatRealtimeClient.cs`, `ChatSyncService.cs`
- Create: `src/Messenger.Maui/Features/Contacts/*.xaml`, `*.cs`
- Create: `src/Messenger.Maui/Features/Chats/*.xaml`, `*.cs`
- Test: `tests/Messenger.Api.IntegrationTests/Realtime/SignalRTests.cs`
- Test: `tests/Messenger.Maui.Tests/Chats/ChatSyncAndViewModelTests.cs`

**Interfaces:**
- Produces: authenticated `/hubs/chat`, six approved server events, `IChatRealtimeClient`, minimal contacts/chat/folder/group/profile UI.
- Consumes: Tasks 6-7 contracts and reconciliation cursors.

- [ ] **Step 1: Write failing hub tests** proving only active members join a chat group and events publish only after committed message transactions.
- [ ] **Step 2: Write the Review Focus failing reconnect test:** save cursor N, omit N+1/N+2 SignalR events, reconnect, reconcile both exactly once in sequence.
- [ ] **Step 3: Run tests, implement hub/publisher/sync services, and rerun tests.**
- [ ] **Step 4: Write failing view-model tests** for folder reorder, edit-mode bulk actions, direct/group creation, unread badges, composer preservation, search, pins, contact alias/mute, and `tel:` launcher invocation.
- [ ] **Step 5: Implement minimal XAML/view models and SignalR reconnect UI states; run MAUI tests and Android build.**
- [ ] **Step 6: Run the full suite and commit with `git commit -m "feat: add realtime chat and MAUI conversations"`.**

---

## Milestone 3: Media and music

### Task 9: Secure upload pipeline and chat media client

**Files:**
- Create: `src/Messenger.Domain/Media/StoredObject.cs`, `UploadSession.cs`, `MessageAttachment.cs`
- Create: `src/Messenger.Application/Media/IObjectStore.cs`, `IFileInspector.cs`, `IMalwareScanner.cs`, `IUploadService.cs`
- Create: `src/Messenger.Infrastructure/Media/MinioObjectStore.cs`, `FileSignatureInspector.cs`, `DevelopmentMalwareScanner.cs`
- Create: `src/Messenger.Api/Endpoints/UploadEndpoints.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_Media.cs`
- Create: `src/Messenger.Maui/Services/MediaCaptureService.cs`, `UploadClient.cs`
- Modify: chat composer files from Task 8
- Modify: profile/group view models to attach authorized avatar object IDs
- Test: `tests/Messenger.Api.IntegrationTests/Media/UploadEndpointTests.cs`
- Test: `tests/Messenger.Maui.Tests/Media/MediaComposerTests.cs`

**Interfaces:**
- Produces: upload-session/create/complete/download/media-list flow and MAUI file/gallery/camera/audio acquisition.
- Consumes: Task 7 message attachment contract and Task 6 authorization.

- [ ] **Step 1: Write failing upload tests** for expiry, private objects, authorized short-lived URLs, checksum validation, and orphan cleanup.
- [ ] **Step 2: Write the Review Focus failing table test** for oversize files, MIME/extension/magic-byte mismatch, wrong checksum, and `NotScanned` never becoming a production-approved object.
- [ ] **Step 3: Run tests, implement MinIO/upload/inspection services, and rerun integration tests against real MinIO.**
- [ ] **Step 4: Write failing MAUI tests** for permission denial, cancellation, progress, retry, captured photo/video, file selection, audio-recording metadata, and profile/group-avatar assignment.
- [ ] **Step 5: Implement platform services and composer attachment UI; run tests and Android build.**
- [ ] **Step 6: Run the full suite and commit with `git commit -m "feat: add secure media uploads and capture"`.**

### Task 10: Music catalog, library, rights, claims, and moderation

**Files:**
- Create: `src/Messenger.Domain/Music/MusicTrack.cs`, `RightsDeclaration.cs`, `CopyrightClaim.cs`, `ModerationAction.cs`
- Create: `src/Messenger.Application/Music/IMusicService.cs`, `IMusicModerationService.cs`
- Create: `src/Messenger.Api/Endpoints/MusicEndpoints.cs`, `AdminMusicEndpoints.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_Music.cs`
- Create: `src/Messenger.Contracts/Music/*.cs`
- Test: `tests/Messenger.Domain.Tests/Music/MusicStateTests.cs`
- Test: `tests/Messenger.Api.IntegrationTests/Music/MusicEndpointTests.cs`

**Interfaces:**
- Produces: search/library/upload/declaration/stream/download/claim/moderation operations and append-only audit records.
- Consumes: Task 9 object storage and signed URL authorization.

- [ ] **Step 1: Write failing domain tests** for `Pending -> Available -> Blocked/Deleted`, declaration required before review, immutable audit actions, and duplicate library additions.
- [ ] **Step 2: Write failing endpoint tests** for global search/personal library, uploader ownership, claim intake, admin-only decisions, and inaccessible object IDs.
- [ ] **Step 3: Write the Review Focus failing test:** block an available track between authorization attempts; all later stream/download requests fail and no new signed URL is issued.
- [ ] **Step 4: Run tests, implement services/endpoints, rerun focused and full suites.**
- [ ] **Step 5: Commit with `git commit -m "feat: add moderated music library"`.**

### Task 11: MAUI music library, downloader, and compact player

**Files:**
- Create: `src/Messenger.Maui/Features/Music/MusicPage.xaml`, `MusicViewModel.cs`, `UploadTrackPage.xaml`, `UploadTrackViewModel.cs`
- Create: `src/Messenger.Maui/Services/MusicDownloadService.cs`, `AudioPlayerService.cs`
- Test: `tests/Messenger.Maui.Tests/Music/MusicViewModelTests.cs`
- Test: `tests/Messenger.Maui.Tests/Music/AudioPlayerTests.cs`

**Interfaces:**
- Produces: global search, personal library, upload declaration, app-private download/remove, and compact player state.
- Consumes: Task 10 contracts and Task 9 upload client.

- [ ] **Step 1: Write failing view-model tests** for debounced search, add/remove, declaration gating, download progress, local-file removal, and play/pause/seek/time formatting.
- [ ] **Step 2: Write the Review Focus failing client test:** a playing track receives `Blocked`; playback stops, stale download authorization is discarded, and the UI exposes the reason.
- [ ] **Step 3: Run tests, implement services/view models/minimal XAML, and rerun tests.**
- [ ] **Step 4: Verify app-private storage and lifecycle pause/resume in an Android smoke run.**
- [ ] **Step 5: Run the full suite and commit with `git commit -m "feat: add MAUI music player and downloads"`.**

---

## Milestone 4: Settings, support, deletion, hardening, and delivery

### Task 12: Settings UI, support, and account deletion

**Files:**
- Create: `src/Messenger.Domain/Support/SupportTicket.cs`, `SupportMessage.cs`
- Create: `src/Messenger.Domain/Users/AccountDeletionRequest.cs`
- Create: `src/Messenger.Application/Support/ISupportService.cs`
- Create: `src/Messenger.Application/Users/IAccountDeletionService.cs`
- Create: `src/Messenger.Api/Endpoints/SupportEndpoints.cs`, `AdminSupportEndpoints.cs`
- Create: `src/Messenger.Infrastructure/Persistence/Migrations/*_SupportAndDeletion.cs`
- Extend: `src/Messenger.Api/Endpoints/ProfileEndpoints.cs`
- Create: `src/Messenger.Maui/Features/Settings/*.xaml`, `*.cs`
- Test: `tests/Messenger.Application.Tests/Support/SupportAndDeletionTests.cs`
- Test: `tests/Messenger.Maui.Tests/Settings/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: support ticket conversation/admin close, 30-day deletion scheduling/cancellation/anonymization, profile/privacy/session/2FA/settings screens.
- Consumes: Tasks 3-4 identity/profile contracts.

- [ ] **Step 1: Write failing tests** for ticket ownership/admin access, closed-ticket behavior, Review Focus cross-user ticket IDs returning `404`, immediate session revocation on deletion request, 30-day schedule, cancellation, and retention-aware anonymization.
- [ ] **Step 2: Run tests, implement support/deletion services/endpoints/background job, and rerun tests.**
- [ ] **Step 3: Write failing MAUI tests** for avatar/name/phone/username/bio edits, privacy, session revoke, email 2FA, support conversation, deletion warning/request/cancel.
- [ ] **Step 4: Implement minimal settings/support XAML and view models; rerun MAUI tests and Android build.**
- [ ] **Step 5: Run the full suite and commit with `git commit -m "feat: add settings support and account deletion"`.**

### Task 13: Security hardening, documentation, and end-to-end verification

**Files:**
- Create: `src/Messenger.Api/Middleware/ProblemDetailsMiddleware.cs`, `CorrelationIdMiddleware.cs`, `SensitiveDataLoggingFilter.cs`
- Create: `tests/Messenger.EndToEndTests/*`
- Create: `README.md`, `docs/API.md`, `docs/SECURITY.md`, `docs/MUSIC-COPYRIGHT.md`, `docs/IOS-BUILD.md`
- Create: `scripts/verify.ps1`
- Modify: Android network security config, iOS Info.plist/entitlements, API production options, Compose health checks

**Interfaces:**
- Produces: stable Problem Details codes, correlation IDs, release transport policy, full startup/verification documentation.
- Consumes: every earlier task.

- [ ] **Step 1: Write failing API tests** for `401/403/404/409/422/429`, correlation IDs, HSTS/HTTPS production behavior, redacted logs, and Debug-only cleartext configuration.
- [ ] **Step 2: Run tests, implement middleware/options/platform hardening, and rerun them.**
- [ ] **Step 3: Write end-to-end tests** for registration/login/refresh replay, direct/group messaging, reconnect reconciliation, read/archive/folders, media, music declaration/blocking, email 2FA with a test SMTP server, support, and deletion/cancellation.
- [ ] **Step 4: Run the E2E tests against Docker Compose and fix only behavior exposed by failing tests, preserving the red-green cycle for every defect.**
- [ ] **Step 5: Add README, OpenAPI export instructions, environment/secret setup, Mail.ru SMTP setup without credentials, Android run steps, iOS-on-Mac steps, music/legal operational notes, and `scripts/verify.ps1`.**
- [ ] **Step 6: Run `scripts/verify.ps1`; it must execute format checking, restore, complete tests, API build, Android build, Compose health smoke, migration check, and secret scan with zero failures.**
- [ ] **Step 7: On a Mac executor, run the documented iOS build/simulator smoke test; if none is available, report iOS as unverified rather than passing.**
- [ ] **Step 8: Review the implementation line-by-line against the approved spec, record any explicit environmental limitation, and commit with `git commit -m "test: verify messenger MVP end to end"`.**

## Plan completion criteria

- Every task has its focused red-green evidence and ends with the complete suite green.
- PostgreSQL and MinIO integration tests use real containers, not mocks.
- Android has a fresh successful build and smoke run.
- iOS has a fresh Mac build result or is explicitly reported as unverified.
- Docker Compose services are healthy and the documented local path works from a clean checkout.
- No credential or user-provided SMTP password appears anywhere in Git history or generated logs.
- The final report separates implemented technical controls from legal/organizational obligations, and includes the requested music implementation summary.
