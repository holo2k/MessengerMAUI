# Messenger MVP Design

## 1. Purpose and success criteria

Build a runnable messenger MVP with an ASP.NET Core backend and a .NET MAUI client for Android and iOS. The interface is intentionally minimal: it must expose and demonstrate the requested behavior, while a separate visual redesign can replace it later without rewriting application services.

The MVP is successful when a developer can start PostgreSQL, MinIO, and the API locally, run the Android client, register with a phone number and development code, maintain a refresh-token session, manage contacts, exchange messages in direct and group chats in real time, upload attachments, use chat folders, manage a music library, configure the profile and email 2FA, contact support, and request account deletion.

The iOS target and platform integrations are included in source. Building and signing iOS requires a Mac with a compatible Xcode installation and an Apple development environment; this Windows workstation can verify shared code and Android, but cannot provide final iOS build evidence.

## 2. Confirmed scope

### Included

- Android and iOS MAUI targets.
- Phone-number registration and login using a provider abstraction.
- Development SMS implementation with fixed code `111111`.
- JWT access tokens and rotating refresh-token sessions.
- Direct chats and full group chats with a minimal group-management UI.
- Contacts, local contact aliases, username/phone search, and contact muting.
- Chat archive, read state, per-user deletion, folders, and folder ordering.
- Text messages, files, images, videos, captured photos, and recorded audio.
- Message search, pins, edits, deletion, and read positions.
- SignalR foreground real-time updates and REST reconciliation after reconnect.
- Profile, privacy, active sessions, email 2FA, support, and delayed account deletion.
- Global music search, personal library, upload, streaming, download, and a compact player.
- Technical copyright declarations, claims, moderation, and audit records.
- PostgreSQL, MinIO, Docker Compose, OpenAPI, migrations, and automated tests.
- HTTPS/WSS in production, application-level encryption for sensitive fields, and secure token handling.

### Excluded from this MVP

- End-to-end encryption.
- Real SMS delivery and a contract with a mobile operator.
- FCM, APNs, or any other system push notifications.
- In-app VoIP calls; the call action opens the platform phone dialer.
- A polished visual design system.
- Automatic legal compliance, registration with authorities, or execution of organizational duties on behalf of the service owner.

When the application is closed, no notification is delivered. On the next launch or SignalR reconnect, the client fetches missed changes from the REST API.

## 3. Architecture choice

Use a feature-oriented modular monolith. One ASP.NET Core deployment contains isolated Identity, Users, Contacts, Chats, Media, Music, Settings, and Support modules. The modules share PostgreSQL and MinIO but expose behavior through application interfaces rather than by reaching into one another's persistence logic.

The solution contains these projects:

- `Messenger.Domain`: entities, value objects, enums, and domain rules.
- `Messenger.Application`: use cases, authorization policies, ports, and validators.
- `Messenger.Infrastructure`: EF Core, PostgreSQL, MinIO, encryption, SMTP, token issuance, and provider implementations.
- `Messenger.Api`: HTTP endpoints, SignalR hubs, authentication, OpenAPI, middleware, and composition root.
- `Messenger.Contracts`: versioned request/response DTOs and SignalR event contracts; no EF entities.
- `Messenger.Maui`: pages, view models, client services, platform integrations, and resources.
- Test projects for domain/application units, API integration, infrastructure integration, and MAUI view models.

Use .NET 10, ASP.NET Core, EF Core, PostgreSQL, SignalR, MinIO, and .NET MAUI with MVVM. The client may reference `Messenger.Contracts`, but it never references Domain, Application, or Infrastructure.

## 4. Responsibility split

### ASP.NET Core owns

- User identity, login challenges, sessions, permissions, and token rotation.
- Canonical profiles, contacts, local aliases, and privacy filtering.
- Direct/group chat rules, memberships, roles, folders, archive, mute, and read state.
- Message ordering, idempotency, encryption, search indexes, pins, edits, deletion, and SignalR events.
- File metadata, upload sessions, object authorization, and signed MinIO URLs.
- Music catalog, personal libraries, rights declarations, claims, and moderation.
- Email 2FA, support tickets, deletion requests, audit logs, and retention hooks.

### .NET MAUI owns

- Country-code selection and user-friendly phone input validation.
- Navigation, rendering, loading/error state, and retry affordances.
- Access/refresh lifecycle orchestration and secure local token storage.
- SignalR reconnect and REST synchronization.
- File/media selection, camera capture, microphone recording, playback, and local downloads.
- Opening `tel:` through the operating system.
- A small non-secret UI-state cache; message bodies are not persistently cached in the MVP.

## 5. Domain and persistence model

All identifiers are UUIDs. Timestamps are UTC. Mutable aggregates use optimistic concurrency. Phone numbers are normalized to E.164 before persistence.

### Identity and users

- `User`: encrypted phone, phone blind index, first/last name, nullable case-insensitive unique username, bio, avatar object ID, status, creation and deletion timestamps.
- `LoginChallenge`: phone blind index, purpose, code hash, expiry, attempt count, consumed timestamp.
- `RefreshSession`: user, device label, token-family ID, refresh-token hash, expiry, replacement/revocation metadata.
- `UserSecuritySettings`: 2FA email, email verification state, 2FA enabled state.
- `UserPrivacySettings`: phone, avatar, and last-seen visibility values.

The development SMS provider accepts `111111`. It is unavailable outside `Development`; production startup must fail if no real provider is configured.

### Contacts

- `Contact`: owner user, target user, optional local first/last name, mute state, creation timestamp.
- A user cannot add themselves or create a duplicate contact.
- A local alias is visible only to its owner and never mutates the target user's profile.

### Chats and messages

- `Chat`: `Direct` or `Group`, group title/avatar, creator, current message sequence, timestamps.
- `DirectChatPair`: canonical ordered user pair with a unique constraint.
- `ChatMember`: chat, user, owner/admin/member role, joined/left timestamps, mute, archive, hidden-at, and last-read sequence.
- `ChatFolder`: user, title, ordered position.
- `ChatFolderItem`: folder/chat relation with a uniqueness constraint.
- `Message`: chat, sender, sequence, client message ID, type, encrypted body, nonce, key version, timestamps, global-deletion metadata.
- `MessageSearchToken`: message ID and keyed HMAC token for normalized complete-word search.
- `MessageAttachment`: message, stored object, media kind, display name, MIME, size, duration, dimensions, and thumbnail.
- `HiddenMessage`: per-user hiding of a message.
- `PinnedMessage`: chat/message relation and pinning actor.

`(sender, clientMessageId)` is unique, making retries idempotent. Read status is derived from a member's last-read sequence rather than rows per message.

Deleting a chat hides it only for the requesting member. A new incoming message makes a hidden direct chat visible again. Group members can leave; only the owner can delete a group. Messages can be globally deleted by their sender within 48 hours, and can otherwise be hidden only for the requester.

### Files and music

- `StoredObject`: owner, MinIO key, purpose, MIME, size, SHA-256 checksum, scan state, lifecycle state, timestamps.
- `UploadSession`: requester, intended purpose/type/size, expiry, completion state.
- `MusicTrack`: audio object, cover object, title, artist, duration, uploader, `Pending/Available/Blocked/Deleted` state.
- `UserMusic`: user/track membership.
- `RightsDeclaration`: uploader, track, declaration version/text hash, accepted timestamp, session/IP audit reference.
- `CopyrightClaim`: track, claimant details, basis, evidence references, state, timestamps.
- `ModerationAction`: append-only actor, action, reason, before/after state, timestamp.

A new track is not available to other users until it is `Available`. Blocked tracks cannot be streamed or downloaded. Local download state belongs to the MAUI client, not the server.

### Support and account deletion

- `SupportTicket`: author, subject, status, timestamps.
- `SupportMessage`: ticket, author/admin identity, body, timestamp.
- `AccountDeletionRequest`: user, request time, scheduled deletion time, cancellation/completion state.

Account deletion immediately deactivates login and revokes sessions. The default grace period is 30 days. After it expires, personal data is anonymized unless a separately configured legal-retention rule requires preservation.

## 6. Encryption and secret handling

- Production transport is HTTPS and WSS only. Enable HSTS and HTTP-to-HTTPS redirection.
- Android release builds disallow cleartext traffic; iOS uses App Transport Security defaults.
- A narrowly scoped Debug-only local exception may be used for an emulator when development certificate trust is unavailable. It must not compile into Release.
- Message bodies and phone numbers use AES-256-GCM with a fresh nonce, authenticated tag, and explicit key version.
- Exact phone lookup and normalized complete-word message search use independent keyed HMAC blind indexes.
- Encryption, HMAC, JWT, and SMTP secrets are separate and supplied through environment/secret providers.
- Development can read secrets from user-secrets or an ignored `.env`; production exposes a KMS-backed key-provider interface.
- Refresh tokens and verification codes are stored as hashes. Refresh-token reuse revokes the token family.
- The MAUI client stores tokens in `SecureStorage`; tokens never appear in URLs or logs.
- Logs exclude message bodies, raw phone numbers, tokens, codes, and SMTP credentials.
- MinIO objects are private and encrypted at rest. Access uses short-lived, authorized signed URLs.

This is server-controlled encryption, not E2E. The server can decrypt authorized data for search and legally required server operations.

## 7. HTTP API

All routes are rooted at `/api`. Responses use versioned contracts. Errors use RFC Problem Details plus a stable `code` and correlation ID. Collection endpoints use cursor pagination.

### Authentication

- `POST /auth/challenges`
- `POST /auth/register`
- `POST /auth/login`
- `POST /auth/2fa/confirm`
- `POST /auth/refresh`
- `POST /auth/logout`
- `POST /auth/logout-all`

Access tokens last 15 minutes. Refresh sessions last 30 days and rotate on every use.
When email 2FA is enabled, `/auth/login` returns a short-lived, purpose-bound pending token rather than an authenticated session. `/auth/2fa/confirm` consumes that token and a valid email code before issuing access and refresh tokens.

### Profile and settings

- `GET|PATCH /me`
- `POST|DELETE /me/avatar`
- `POST /me/phone/challenges`
- `PUT /me/phone`
- `GET|PUT /me/privacy`
- `GET /me/sessions`
- `DELETE /me/sessions/{sessionId}`
- `POST /me/2fa/email`
- `POST /me/2fa/email/confirm`
- `POST /me/2fa/disable`
- `POST|DELETE /me/deletion`

Phone changes and account deletion require a recent confirmation challenge. Email 2FA sends a one-time code through SMTP.

### Contacts and discovery

- `GET|POST /contacts`
- `PATCH|DELETE /contacts/{contactId}`
- `GET /users/search?query=`
- `GET /users/{userId}`

User search accepts an exact normalized phone or username. Response fields obey target-user privacy settings.

### Chats, members, folders, and messages

- `GET|POST /chats`
- `POST /chats/direct`
- `POST /chats/groups`
- `GET|PATCH|DELETE /chats/{chatId}`
- `GET|POST /chats/{chatId}/members`
- `PATCH|DELETE /chats/{chatId}/members/{userId}`
- `PUT /chats/{chatId}/archive`
- `PUT /chats/{chatId}/mute`
- `PUT /chats/{chatId}/read`
- `GET|POST /chat-folders`
- `PATCH|DELETE /chat-folders/{folderId}`
- `PUT /chat-folders/order`
- `PUT /chat-folders/{folderId}/chats`
- `GET|POST /chats/{chatId}/messages`
- `GET /chats/{chatId}/messages/search`
- `PATCH|DELETE /messages/{messageId}`
- `PUT|DELETE /messages/{messageId}/pin`
- `GET /chats/{chatId}/pins`
- `GET /chats/{chatId}/media`

Commands enforce membership and role policies. Message creation is a REST command; only after its transaction commits does the server publish a SignalR event.

### Uploads and downloads

- `POST /uploads`
- `POST /uploads/{uploadId}/complete`
- `GET /files/{fileId}/download`

The upload flow validates declared purpose/type/size, issues a short-lived upload URL, verifies checksum and actual file signature at completion, and then makes the object addressable by a domain record.

### Music

- `GET /music/search`
- `GET /music/library`
- `POST|DELETE /music/library/{trackId}`
- `POST /music/uploads`
- `POST /music/uploads/{uploadId}/declaration`
- `GET /music/tracks/{trackId}/stream`
- `GET /music/tracks/{trackId}/download`
- `POST /music/tracks/{trackId}/claims`
- `GET /admin/music/claims`
- `POST /admin/music/claims/{claimId}/decision`
- `POST /admin/music/tracks/{trackId}/moderation`

### Support

- `GET|POST /support/tickets`
- `GET|POST /support/tickets/{ticketId}/messages`
- `GET /admin/support/tickets`
- `POST /admin/support/tickets/{ticketId}/messages`
- `POST /admin/support/tickets/{ticketId}/close`

## 8. SignalR contract

The authenticated hub is `/hubs/chat`. A connection joins only groups for chats in which the authenticated user is an active member.

Server-to-client events are:

- `MessageCreated`
- `MessageUpdated`
- `MessageDeleted`
- `ReadPositionChanged`
- `ChatChanged`
- `MemberChanged`

SignalR is an acceleration channel, not the source of truth. Events contain stable resource IDs and cursors. After initial connection and every reconnect, the MAUI client calls REST endpoints with its last known cursor to reconcile changes. A missed SignalR event therefore cannot permanently lose data.

## 9. MAUI application

Use Shell navigation with four tabs: Chats, Contacts, Music, and Settings. Each feature has focused Page, ViewModel, and service types. ViewModels depend on interfaces and remain platform-independent.

### Session behavior

- Country code selection and phone input are client-side concerns; the server still normalizes and validates.
- Register/login requests a challenge and accepts `111111` in Development.
- An HTTP handler adds the access token and coordinates a single refresh attempt across concurrent requests.
- Refresh failure clears local secrets and navigates to login.
- SignalR uses the current access token, reconnects with backoff, then triggers REST reconciliation.

### Chats UI

- Ordered horizontal folder strip with create/edit/reorder actions.
- Chat cards show avatar, title, last text or attachment kind, time, and unread state.
- Edit mode supports archive, mark read, and per-user delete.
- New chat flow creates a direct chat or selects multiple contacts for a titled group.
- Conversation page shows ordered bubbles, composer, attachment menu, search, and pinned messages.
- Attachment menu exposes file, gallery, camera, video, and audio recording subject to platform permissions.
- Contact/group profile exposes avatar, names, phone when permitted, bio, shared media, pins, mute, local alias, message action, and system dialer action.

### Contacts UI

- Contact list with local filter.
- Exact server search by phone or username.
- Add, rename locally, mute, delete, and open the shared profile page.

### Music UI

- Global search and personal library lists with cover, title, artist, and duration.
- Add/remove, download, remove local copy, and upload actions.
- Upload requires an explicit rights declaration before completion.
- A compact bottom player shows cover, title/artist, progress, elapsed/total time, and play/pause.
- Downloaded files live inside app storage and are not exported to public media folders by default.

### Settings UI

- Avatar, name, phone, username, and bio editing.
- Privacy, active sessions, and email 2FA.
- Support ticket list/conversation.
- Account deletion warning, request, and cancellation during the grace period.

## 10. Email delivery

Use an `IEmailSender` application port and SMTP infrastructure adapter. The intended provider is Mail.ru SMTP at `smtp.mail.ru:465` using SSL/TLS and an application-specific password. Configuration contains only the sender address and the name of the secret variable; no password is committed.

Email verification codes are random, short-lived, single-use, attempt-limited, and stored only as hashes. Sending is rate-limited. SMTP failures do not enable 2FA and return a retryable error without exposing provider details.

## 11. Validation, authorization, and failure handling

- Validate DTO shape, normalized values, text lengths, membership limits, folder limits, and configured media sizes at API boundaries.
- Re-check ownership/membership for every chat, message, file, track, ticket, and administrative action.
- Object IDs and signed URLs do not substitute for authorization.
- Use `401` for missing/invalid authentication, `403` for known-but-forbidden operations, `404` to avoid exposing inaccessible resources, `409` for concurrency/state conflicts, `422` for semantic validation, and `429` for rate limits.
- Network failure in MAUI preserves unsent composer text. A retry reuses the same client message ID.
- Upload sessions expire and incomplete MinIO objects are cleaned by a background job.
- File checks include declared MIME, extension, magic bytes, size, and SHA-256. Malware scanning is an interface; a development no-op scanner reports an explicit `NotScanned` state and must not masquerade as a successful scan.
- Rate-limit login challenges, code attempts, discovery, message creation, uploads, claims, and support submission.

## 12. Infrastructure and configuration

Docker Compose runs PostgreSQL, MinIO, and the ASP.NET API. It defines health checks, named volumes, private service networking, and development-only defaults. `.env.example` documents required variable names without values; `.env` is ignored.

EF Core migrations are explicit. Development documentation provides commands to create/apply migrations. Production does not silently mutate its schema at ordinary application startup.

The repository includes OpenAPI output, local startup instructions, safe seed data, and instructions for trusting a development HTTPS certificate. Android can be built after installing the MAUI workload. iOS source is maintained but requires a Mac/Xcode for build, signing, simulator, and device verification.

## 13. Testing strategy

- Domain unit tests cover role transitions, deletion windows, read positions, token-family rules, track states, and account-deletion scheduling.
- Application tests cover authorization, idempotency, privacy filtering, encrypted-search behavior, and failure mapping with real domain objects.
- API integration tests run against real PostgreSQL and MinIO containers and exercise authentication, migrations, signed object access, SignalR publication after commit, and cross-user access denial.
- Infrastructure tests cover AES-GCM round trips/tamper rejection, blind indexes, SMTP message construction without sending real mail, refresh-token hashing, and upload signature validation.
- MAUI view-model tests cover navigation state, refresh coordination, reconnect reconciliation, composer preservation, folder ordering, player state, and permission-denied behavior.
- End-to-end scenarios cover registration/login, refresh rotation and reuse rejection, direct/group messaging, read state, archive/folders, media upload, music declaration/moderation, email 2FA, support, and account deletion/cancellation.

Final verification requires a clean restore/build, the complete automated test suite, Docker Compose health, an Android smoke run, secret scanning, and confirmation that no Release manifest permits cleartext traffic. iOS build verification is reported separately and cannot be claimed without a Mac executor.

## 14. Music copyright and Russian-law boundary

The uploader must affirm that they own or are authorized to distribute a track. Store the declaration version and audit record. Provide a claimant intake flow, evidence references, immediate administrative blocking, a decision record, and an append-only moderation trail. Blocking prevents new streams and downloads; deletion follows the configured retention and evidence policy.

This workflow reduces operational risk but a disclaimer alone does not remove liability. A rights holder can still seek removal or access restriction. Before public launch, the owner needs Russian legal advice, a published takedown procedure, verified operator/contact details, and staff/processes for claims.

The messenger may also be treated as an organizer of information dissemination and an instant-messaging service under Russian law. Code can support identification records, retention configuration, access control, audit, and exports, but it cannot register the operator, execute agreements, or complete interactions with authorities. The fixed SMS code and development-only provider are not production identification.

Legal references used for this design boundary:

- Civil Code of the Russian Federation, Article 1253.1: <https://www.consultant.ru/document/cons_doc_LAW_64629/eb6ec591cb78fe25054cd4b9e0dbcc79abcf0d3a/>
- Federal Law No. 149-FZ, Article 10.1: <https://www.consultant.ru/document/cons_doc_LAW_61798/9ab7abe2b9fe407f507610f7e6e14a951d575585/>
- Mail.ru official SMTP settings: <https://help.mail.ru/mail/login/mailer/>

## 15. Delivery sequence

Implement as one integrated solution in four independently testable milestones:

1. Solution foundation, containers, identity, profiles, encryption, sessions, SMTP 2FA, and MAUI authentication shell.
2. Contacts, direct/group chats, messages, folders, SignalR, read state, and core chat UI.
3. Upload pipeline, chat media, camera/audio integrations, music library, player, downloads, claims, and moderation API.
4. Settings, privacy, support, account deletion, hardening, documentation, and full verification.

Each milestone follows test-driven development. Product code is written only after the relevant behavior test has been observed failing for the expected reason.
