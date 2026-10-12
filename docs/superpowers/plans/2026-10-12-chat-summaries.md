# Chat Summaries and Peer Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Личные диалоги показывают правильного собеседника, профиль открывается по его идентификатору, а последнее сообщение и его время либо дата автоматически обновляются и сохраняются у обоих участников.

**Architecture:** ASP.NET формирует единый read model чата с `Peer` и `LastMessage`, получая связанные данные пакетно на сервере. После фиксации сообщения скрытый личный диалог адресата восстанавливается, SignalR сообщает клиенту идентификатор изменившегося чата, а MAUI точечно перечитывает карточку и выполняет upsert без клиентского N+1.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core/PostgreSQL, SignalR, .NET MAUI, xUnit, Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-12-chat-summaries-design.md`

## Global Constraints

- Новая таблица и миграция БД не создаются; используются существующие пользователи, профили, контакты, участники и сообщения.
- Для личного чата `Peer` — другой активный участник; для группы `Peer = null`, используются название и аватар группы.
- Приоритет имени: локальное имя контакта → имя и фамилия профиля → username → видимый номер телефона → `Пользователь`.
- Последнее сообщение расшифровывается только на сервере; ciphertext не выходит в API.
- Сегодняшнее сообщение в списке имеет только `HH:mm`, более старое — только `dd.MM.yy`; внутри чата используется `dd.MM.yyyy HH:mm`.
- Событие SignalR публикуется только после успешного сохранения сообщения и восстановления скрытого личного диалога.
- FCM, APNs, E2E-шифрование и визуальный редизайн не входят в изменение.

## Review Focus

- Повреждённое зашифрованное тело последнего сообщения не должно ломать весь список: Task 1 закрепляет безопасный fallback `Сообщение` интеграционным тестом.
- Пустые имя, username и скрытый номер должны давать `Пользователь`, не пустой заголовок: Task 1 проверяет всю цепочку fallback.
- Повторное SignalR-событие и повторный `OnAppearing` не должны создавать карточки или подписки-дубликаты: Task 4 проверяет идемпотентность.
- Переход через полночь в локальной временной зоне должен переключать `HH:mm` на `dd.MM.yy`: Task 3 проверяет обе стороны границы даты.
- Участник, покинувший группу, не должен возвращаться из-за нового сообщения: Task 2 проверяет, что восстановление применяется только к скрытым участникам direct-чата.

---

### Task 1: Серверный read model чата и контракт API

**Files:**
- Create: `src/Messenger.Application/Chats/ChatSummary.cs`
- Create: `src/Messenger.Infrastructure/Social/EfChatSummaryReader.cs`
- Modify: `src/Messenger.Contracts/Chats/ChatContracts.cs`
- Modify: `src/Messenger.Application/Chats/IChatService.cs`
- Modify: `src/Messenger.Application/Chats/ChatService.cs`
- Modify: `src/Messenger.Api/Endpoints/ChatEndpoints.cs`
- Modify: `src/Messenger.Api/Program.cs`
- Modify: `tests/Messenger.Application.Tests/Chats/ContactChatServiceTests.cs`
- Modify: `tests/Messenger.Api.IntegrationTests/Chats/ChatAndContactEndpointTests.cs`

**Interfaces:**
- Produces: `ChatPeerResponse(Guid UserId, string DisplayName, string? AvatarObjectId)`.
- Produces: `ChatLastMessageResponse(Guid Id, Guid SenderUserId, string Type, string? Body, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt)`.
- Produces: `ChatResponse.Peer` and `ChatResponse.LastMessage`.
- Produces: `IChatSummaryReader.GetAsync(Guid requesterUserId, Guid chatId, CancellationToken)` and `ListAsync(Guid requesterUserId, DateTimeOffset? afterUpdatedAt, Guid? afterId, int take, CancellationToken)` returning enriched `ChatView` values.
- Produces: `ChatDisplayName.Resolve(string? localFirstName, string? localLastName, string firstName, string lastName, string? username, string? visiblePhone) -> string`.
- Consumes: existing `IFieldCipher`, privacy settings, hidden-message rows, chat cursor ordering and domain entities.

- [ ] **Step 1: Write failing name-resolution unit tests**

Add tests named `Chat_display_name_prefers_local_alias_then_profile_username_phone_and_fallback` that assert every priority level, trimming/combining first and last names, and final `Пользователь` when all inputs are empty.

- [ ] **Step 2: Run the focused application test and verify failure**

Run: `dotnet test tests/Messenger.Application.Tests/Messenger.Application.Tests.csproj --filter FullyQualifiedName~ContactChatServiceTests.Chat_display_name`

Expected: FAIL because `ChatDisplayName` does not exist.

- [ ] **Step 3: Add the public response records and application read-model interfaces**

Extend `ChatResponse` with nullable `ChatPeerResponse? Peer` and `ChatLastMessageResponse? LastMessage`. Define `ChatPeerView`, `ChatLastMessageView`, enriched `ChatView`, `ChatSummaryPage`, `IChatSummaryReader`, and the exact `ChatDisplayName.Resolve` signature in `ChatSummary.cs`.

- [ ] **Step 4: Implement `ChatDisplayName.Resolve` minimally and rerun its test**

Run the command from Step 2.

Expected: PASS for the complete priority chain.

- [ ] **Step 5: Write failing API integration tests for enriched list/get responses**

Add `Chat_list_and_get_return_owner_local_peer_and_latest_message` and assert:

- the same direct chat is visible to both users;
- each response has the other user in `Peer.UserId`;
- the owner's local alias wins only in that owner's response;
- the latest visible message has the correct ID, sender, type, plaintext body and `CreatedAt`;
- a group response has `Peer == null` and keeps its own title;
- a profile with no usable name/username/visible phone returns `Пользователь`.

Add `Corrupt_latest_message_uses_safe_preview_without_hiding_other_chats`: alter the last message authentication tag through the test DbContext, call `GET /api/chats`, assert HTTP 200 and `LastMessage.Body == "Сообщение"` for the affected chat while other chats remain present.

- [ ] **Step 6: Run the focused API tests and verify failure**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~Chat_list_and_get_return|FullyQualifiedName~Corrupt_latest_message"`

Expected: FAIL because the API does not return `Peer` or `LastMessage`.

- [ ] **Step 7: Implement `EfChatSummaryReader` and wire it into `ChatService`**

Implement one paged chat query followed by bounded batch queries for peer members/profiles/contacts/privacy/users and latest non-hidden messages. Apply visibility before phone/avatar use, decrypt only the selected last message and visible phone, log cryptographic failures with chat/message IDs but not plaintext/ciphertext, and substitute body `Сообщение` after a last-message decryption failure.

Make `ChatService.GetAsync` and `ListAsync` delegate to the reader while mutation methods continue using `IChatStore`. Preserve the existing cursor format and active-membership/not-found behavior. Register `IChatSummaryReader` in `Program.cs`.

- [ ] **Step 8: Map the enriched read model in every chat endpoint response**

Update `ChatEndpoints.ToResponse(ChatView)` so list, get, create-direct and create-group all return identical `Peer`/`LastMessage` semantics. Keep the existing Swagger endpoint documentation; the new records must appear in generated response schemas.

- [ ] **Step 9: Run server tests**

Run: `dotnet test tests/Messenger.Application.Tests/Messenger.Application.Tests.csproj && dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj --filter FullyQualifiedName~Chats`

Expected: PASS, including the new read-model and fallback tests.

- [ ] **Step 10: Commit Task 1**

```powershell
git add src/Messenger.Contracts/Chats/ChatContracts.cs src/Messenger.Application/Chats src/Messenger.Infrastructure/Social/EfChatSummaryReader.cs src/Messenger.Api/Endpoints/ChatEndpoints.cs src/Messenger.Api/Program.cs tests/Messenger.Application.Tests/Chats/ContactChatServiceTests.cs tests/Messenger.Api.IntegrationTests/Chats/ChatAndContactEndpointTests.cs
git commit -m "feat: return peer and last message in chat summaries"
```

### Task 2: Автоматическое восстановление личного диалога при сообщении

**Files:**
- Modify: `src/Messenger.Application/Chats/IChatService.cs`
- Modify: `src/Messenger.Application/Chats/ChatService.cs`
- Modify: `src/Messenger.Application/Chats/MessageService.cs`
- Modify: `tests/Messenger.Application.Tests/Chats/MessageServiceTests.cs`
- Modify: `tests/Messenger.Api.IntegrationTests/Chats/MessageEndpointTests.cs`

**Interfaces:**
- Consumes: existing `IChatStore.ListMembersAsync`, `ChatMember.Reactivate()` and the single `SaveChangesAsync` performed by `MessageService.SendAsync`.
- Produces: successful first-time direct-message send clears `HiddenAt` for every other active participant before persistence completes.
- Removes: unused `IChatService.ReactivateDirectMembersAsync` and its `ChatService` implementation after behavior moves into the message transaction.

- [ ] **Step 1: Write failing application tests for recipient restoration**

Add `Direct_message_reactivates_hidden_recipient_before_save` and assert the recipient's `HiddenAt` becomes null after `SendAsync`. Add `Group_message_does_not_reactivate_members_who_left` and assert `LeftAt` remains unchanged.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test tests/Messenger.Application.Tests/Messenger.Application.Tests.csproj --filter "FullyQualifiedName~Direct_message_reactivates|FullyQualifiedName~Group_message_does_not"`

Expected: the direct-chat test FAILS because sending currently leaves the recipient hidden.

- [ ] **Step 3: Implement restoration inside `MessageService.SendAsync`**

After creating the new message and before the existing single `SaveChangesAsync`, inspect the already loaded chat. For `ChatType.Direct`, call `Reactivate()` only on other members whose `IsActive` is true. Do not run this branch for groups. Remove the now-redundant public reactivation method from chat service interfaces and implementation.

- [ ] **Step 4: Rerun application tests**

Run the command from Step 2.

Expected: PASS.

- [ ] **Step 5: Write and run an endpoint integration test**

Add `Incoming_message_restores_hidden_dialog_without_creating_duplicate_chat`: create one direct chat, hide it through `DELETE /api/chats/{id}` as recipient, send as the other user, then list as recipient and assert exactly one chat with the original ID and the new `LastMessage`.

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj --filter FullyQualifiedName~Incoming_message_restores_hidden_dialog`

Expected: PASS after implementation.

- [ ] **Step 6: Commit Task 2**

```powershell
git add src/Messenger.Application/Chats tests/Messenger.Application.Tests/Chats/MessageServiceTests.cs tests/Messenger.Api.IntegrationTests/Chats/MessageEndpointTests.cs
git commit -m "fix: restore hidden direct chats on incoming messages"
```

### Task 3: Карточка чата и форматирование сообщения в MAUI

**Files:**
- Modify: `src/Messenger.Maui/Services/ConversationClient.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatsViewModel.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatsPage.xaml.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatsPage.xaml`
- Modify: `src/Messenger.Maui/Controls/MessageBubble.cs`
- Modify: `tests/Messenger.Maui.Tests/Chats/ChatSyncAndViewModelTests.cs`

**Interfaces:**
- Produces: `IConversationApi.GetChatAsync(Guid chatId, CancellationToken) -> Task<ChatResponse>`.
- Produces: mutable `ChatListItem.Update(ChatResponse chat)` and presentation properties `Title`, `Preview`, `DateText`.
- Consumes: `ChatResponse.Peer`, `ChatResponse.LastMessage` and current session `UserId` supplied to `ChatsViewModel.SetCurrentUser(Guid)`.

- [ ] **Step 1: Write failing presentation tests**

Add tests asserting:

- a direct item uses `Peer.DisplayName`, while a group uses `Title`;
- outgoing text is `Вы: <body>` and incoming text has no prefix;
- image/video/audio/file/deleted types map to `Фото`, `Видео`, `Аудио`, `Файл`, `Сообщение удалено`;
- a message on the current local calendar date formats as `HH:mm`;
- a message one local date earlier formats as `dd.MM.yy`, including a UTC timestamp on the opposite side of local midnight;
- an empty chat has `Пока нет сообщений` and an empty `DateText`.

Use an injected `Func<DateTimeOffset>` on `ChatListItem` for deterministic local-time tests.

- [ ] **Step 2: Run the focused MAUI tests and verify failure**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj --filter FullyQualifiedName~Chat_list_item`

Expected: FAIL because the existing item uses generic `Личный чат`, `Переписка` and `UpdatedAt`.

- [ ] **Step 3: Implement chat-list presentation and API lookup**

Add `GetChatAsync` to the interface, HTTP client and test fake using `GET api/chats/{chatId}`. Make `ChatListItem.Chat` replaceable through `Update`, raise property notifications for `Title`, `Preview` and `DateText`, and implement the exact copy/format rules from the spec. `ChatsViewModel.SetCurrentUser` supplies the ID used for `Вы:`.

- [ ] **Step 4: Show server summaries in `ChatsPage`**

Read the current session once during page appearance, call `SetCurrentUser`, and create items from enriched responses. Keep existing folder, archive and hidden filters; sort by `LastMessage.CreatedAt ?? UpdatedAt` descending.

- [ ] **Step 5: Show full date and time inside message bubbles**

Change only the timestamp portion of `MessageBubble` to `dd.MM.yyyy HH:mm`, retaining pinned/edited labels.

- [ ] **Step 6: Run MAUI tests and build Android**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj`

Run: `dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true`

Expected: both commands succeed with zero errors.

- [ ] **Step 7: Commit Task 3**

```powershell
git add src/Messenger.Maui/Services/ConversationClient.cs src/Messenger.Maui/Features/Chats/ChatsViewModel.cs src/Messenger.Maui/Features/Chats/ChatsPage.xaml.cs src/Messenger.Maui/Features/Chats/ChatsPage.xaml src/Messenger.Maui/Controls/MessageBubble.cs tests/Messenger.Maui.Tests/Chats/ChatSyncAndViewModelTests.cs
git commit -m "feat: display peer and last message in chat list"
```

### Task 4: Точечная синхронизация списка через SignalR

**Files:**
- Create: `src/Messenger.Maui/Services/ChatListSyncService.cs`
- Modify: `src/Messenger.Maui/MauiProgram.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatsViewModel.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatsPage.xaml.cs`
- Modify: `tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj`
- Modify: `tests/Messenger.Maui.Tests/Chats/ChatSyncAndViewModelTests.cs`

**Interfaces:**
- Consumes: `IChatRealtimeClient.MessageCreated`, `IChatRealtimeClient.Reconnected` and `IConversationApi.GetChatAsync`.
- Produces: `ChatListSyncService.StartAsync`, `Stop`, `ChatChanged(ChatResponse, MessageCreatedEvent)` and `ReloadRequested()`.
- Produces: `ChatsViewModel.Upsert(ChatResponse chat, bool incrementUnread)` keyed by `ChatResponse.Id`.

- [ ] **Step 1: Write failing sync tests**

Add tests that start the service twice, emit one message event, and assert one API lookup and one callback. Verify `Stop` removes subscriptions. Verify a failed point lookup emits no destructive update and that a later reconnect requests a full reload.

Add view-model tests that upsert a previously absent chat, update an existing item without duplication, increment unread only for an incoming sender, preserve the selected folder filter, and move the updated chat to the top.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj --filter "FullyQualifiedName~Chat_list_sync|FullyQualifiedName~Chat_upsert"`

Expected: FAIL because `ChatListSyncService` and `Upsert` do not exist.

- [ ] **Step 3: Implement `ChatListSyncService`**

Make `StartAsync`/`Stop` idempotent. On `MessageCreated`, fetch exactly one chat and publish it with the source event. Catch lookup failures without removing existing UI state; rely on the next page load or reconnect. On reconnect, invoke `ReloadRequested` once. Reuse the singleton realtime connection rather than creating another `HubConnection`.

- [ ] **Step 4: Implement `ChatsViewModel.Upsert`**

Replace an item's response by ID or insert a new item, derive unread increment from `LastMessage.SenderUserId != currentUserId`, and rerun the active folder/filter/sort projection. Repeated events for the same message/chat must not create duplicate items.

- [ ] **Step 5: Wire page lifecycle and DI**

Register `ChatListSyncService`, inject it into `ChatsPage`, attach handlers before `StartAsync`, detach them and call `Stop` from `OnDisappearing`. Point changes call `Upsert`; reconnect calls `LoadAsync`. Ensure repeated `OnAppearing` does not duplicate handlers.

- [ ] **Step 6: Run MAUI tests**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj`

Expected: PASS, including existing open-chat reconciliation tests.

- [ ] **Step 7: Commit Task 4**

```powershell
git add src/Messenger.Maui/Services/ChatListSyncService.cs src/Messenger.Maui/MauiProgram.cs src/Messenger.Maui/Features/Chats/ChatsViewModel.cs src/Messenger.Maui/Features/Chats/ChatsPage.xaml.cs tests/Messenger.Maui.Tests
git commit -m "feat: sync chat summaries over SignalR"
```

### Task 5: Переход к профилю собеседника и сквозная проверка

**Files:**
- Create: `src/Messenger.Maui/Features/Contacts/UserDetailsViewModel.cs`
- Modify: `src/Messenger.Maui/Services/ConversationClient.cs`
- Modify: `src/Messenger.Maui/Features/Contacts/ContactDetailsPage.xaml`
- Modify: `src/Messenger.Maui/Features/Contacts/ContactDetailsPage.xaml.cs`
- Modify: `src/Messenger.Maui/Features/Contacts/ContactsPage.xaml.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatPage.xaml`
- Modify: `src/Messenger.Maui/Features/Chats/ChatPage.xaml.cs`
- Modify: `src/Messenger.Maui/Features/Chats/ChatsPage.xaml.cs`
- Modify: `tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj`
- Modify: `tests/Messenger.Maui.Tests/Chats/ChatSyncAndViewModelTests.cs`
- Create: `tests/Messenger.Maui.Tests/Contacts/UserDetailsViewModelTests.cs`

**Interfaces:**
- Produces: `IConversationApi.GetVisibleUserAsync(Guid userId, CancellationToken) -> Task<UserSearchResponse>` using `GET api/users/{userId}`.
- Produces: `UserDetailsViewModel.LoadAsync(Guid userId)`, `SaveAliasAsync(...)`, `CallAsync()` and properties for visible profile plus optional owned contact.
- Consumes: `ChatResponse.Peer.UserId`; group chats never invoke peer-profile navigation.

- [ ] **Step 1: Write failing user-details tests**

Add tests that load a user by ID, merge an existing owned contact alias/mute state, hide editing when no contact exists, preserve privacy-hidden phone as unavailable, save aliases only through the owned contact ID, and create the exact `tel:<phone>` URI when a visible number exists.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj --filter FullyQualifiedName~User_details`

Expected: FAIL because the view model and visible-user API method do not exist.

- [ ] **Step 3: Implement visible-user loading and shared profile view model**

Add the HTTP method and test-fake implementations. Implement `UserDetailsViewModel` by combining `GetVisibleUserAsync` with the current user's contacts. Editing/muting is enabled only when an owned contact exists; message and call actions remain available subject to phone privacy.

- [ ] **Step 4: Adapt the existing contact details page**

Bind `ContactDetailsPage` to `UserDetailsViewModel` so both the Contacts tab and a chat header use the same screen. Preserve existing alias editing, mute, message and dialer behavior. Hide edit/mute controls for a non-contact instead of sending an update with an empty contact ID.

- [ ] **Step 5: Make the direct-chat header actionable**

Pass the full `ChatResponse` into `ChatPage`. Set title/avatar from `Peer` for direct chats and from chat title/avatar for groups. Add one tap handler covering the direct header name/avatar; it opens `ContactDetailsPage` for exactly `Peer.UserId`. For `Peer == null`, the handler is disabled and never substitutes the current user.

- [ ] **Step 6: Run the complete automated suite**

Run: `dotnet test Messenger.slnx --no-restore`

Expected: all unit, integration and end-to-end projects pass. If Testcontainers reports Docker unavailable, start Docker and rerun; do not treat skipped infrastructure verification as success.

- [ ] **Step 7: Build and install the Android debug app**

Run:

```powershell
dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Debug -t:Run -p:EmbedAssembliesIntoApk=true "/p:AdbTarget=-s emulator-5554"
```

Expected: build/install succeeds and the app starts without the Fast Deployment `No assemblies found` crash.

- [ ] **Step 8: Execute the agreed two-account scenario**

On the emulator/API:

1. authenticate as `+79089260075` and send a uniquely timestamped message to `+79512723576`;
2. authenticate as `+79512723576` without manually creating a chat;
3. assert exactly one corresponding dialog exists, its title identifies `+79089260075`, and its preview/time are correct;
4. open it and assert both the previously existing message and the new message appear in sequence with `dd.MM.yyyy HH:mm`;
5. tap the header and assert the profile `UserId` belongs to `+79089260075`;
6. hide the dialog as recipient, send again as sender, and assert it automatically returns with the newest preview.

- [ ] **Step 9: Commit Task 5**

```powershell
git add src/Messenger.Maui tests/Messenger.Maui.Tests
git commit -m "feat: open peer profiles from direct chats"
```

- [ ] **Step 10: Review the complete branch diff**

Run: `git status --short` and `git diff 5e98a6f..HEAD --check`

Expected: no unintended files, no whitespace errors, no secrets, no deployment changes, and only MessengerMAUI paths are present.
