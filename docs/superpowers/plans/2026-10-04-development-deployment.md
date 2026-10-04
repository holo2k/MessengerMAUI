# Development Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy the Development ASP.NET API to `api.projectdomain.ru`, expose documented Swagger UI, point MAUI at the public HTTPS service, and add repeatable GitHub Actions deployment to the Ubuntu server.

**Architecture:** Caddy terminates TLS and proxies API, SignalR, and private-bucket object traffic. The self-contained API runs under systemd while PostgreSQL and MinIO run in a loopback-only Docker Compose stack. GitHub Actions tests and publishes immutable releases, then a restricted SSH deployment account invokes an atomic server-side release script.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, Swashbuckle 10.2.3, .NET MAUI, EF Core migration bundles, GitHub Actions, Ubuntu systemd, Docker Compose, PostgreSQL 16, MinIO, Caddy, Bash, PowerShell.

**Spec:** `docs/superpowers/specs/2026-10-04-development-deployment-design.md`

## Global Constraints

- The deployed ASP.NET environment is `Development` and Swagger is publicly reachable only in that environment.
- The primary API base URL is exactly `https://api.projectdomain.ru/`.
- `https://185-56-162-174.sslip.io/` is a temporary verification hostname, not the MAUI release endpoint.
- Only TCP ports 22, 80, and 443 are public.
- PostgreSQL, MinIO, and Kestrel bind to loopback or an internal Docker network.
- Secrets remain in `/etc/messenger/messenger.env`; no application secret enters Git or workflow logs.
- CI/CD uses a dedicated SSH-key-authenticated deploy account, host-key pinning, atomic releases, health checks, and rollback.
- Existing local changes are preserved, and nothing related to M.Video is read, modified, committed, or pushed.
- No remote push occurs unless the repository-specific GitHub authentication and deployment-secret setup are verified.

## Review Focus

- An unlisted CORS origin must receive no `Access-Control-Allow-Origin` header; Task 1 adds the integration assertion.
- Production must not expose Swagger even though Development does; Task 2 adds both environment assertions.
- Every documented API operation must have a non-empty summary and description; Task 3 validates the generated document.
- A malformed or malicious release name must not escape `/opt/messenger/releases`; Task 5 adds shell-level validation checks.
- A failed post-switch health check must restore the prior `current` symlink; Task 5 exercises rollback with a controlled failing release.

---

### Task 1: Configurable CORS

**Files:**
- Create: `src/Messenger.Api/Configuration/CorsSettings.cs`
- Modify: `src/Messenger.Api/Program.cs`
- Modify: `src/Messenger.Api/appsettings.json`
- Modify: `tests/Messenger.Api.IntegrationTests/Auth/AuthEndpointTests.cs`
- Modify: `tests/Messenger.Api.IntegrationTests/Security/HardeningTests.cs`

**Interfaces:**
- Produces: `CorsSettings.SectionName`, `CorsSettings.AllowedOrigins`, and the named ASP.NET policy `MessengerCors`.
- Consumes: `Cors:AllowedOrigins` supplied by JSON or environment variables.

- [ ] **Step 1: Write failing allowed-origin and rejected-origin preflight integration tests**

Add tests that configure `Cors:AllowedOrigins:0=https://trusted.example`, send OPTIONS requests to `/api/auth/challenges`, and assert that only the trusted origin receives `Access-Control-Allow-Origin` and credentials headers.

- [ ] **Step 2: Run the CORS tests and verify RED**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~Cors"`

Expected: FAIL because the application has no CORS policy or middleware.

- [ ] **Step 3: Implement `CorsSettings` and register/use `MessengerCors`**

Allow configured origins, all required methods and headers, and credentials. Treat an empty list as no cross-origin browser access. Place `UseCors` before authentication and endpoint mapping.

- [ ] **Step 4: Run focused and complete integration tests**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj`

Expected: PASS with zero failures.

- [ ] **Step 5: Commit**

```powershell
git add src/Messenger.Api/Configuration/CorsSettings.cs src/Messenger.Api/Program.cs src/Messenger.Api/appsettings.json tests/Messenger.Api.IntegrationTests/Auth/AuthEndpointTests.cs tests/Messenger.Api.IntegrationTests/Security/HardeningTests.cs
git commit -m "feat: configure API cors policy"
```

### Task 2: Development Swagger UI

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Messenger.Api/Messenger.Api.csproj`
- Modify: `src/Messenger.Api/Program.cs`
- Modify: `tests/Messenger.Api.IntegrationTests/Security/HardeningTests.cs`

**Interfaces:**
- Produces: Swagger UI at `/swagger` and OpenAPI JSON at `/swagger/v1/swagger.json` in Development.
- Consumes: Swashbuckle.AspNetCore 10.2.3.

- [ ] **Step 1: Write failing environment-gated Swagger tests**

Assert that Development returns successful HTML from `/swagger/index.html` and JSON from `/swagger/v1/swagger.json`, while Production returns 404 for both paths.

- [ ] **Step 2: Run Swagger tests and verify RED**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~Swagger"`

Expected: FAIL because Swagger UI and its JSON route do not exist.

- [ ] **Step 3: Add Swashbuckle and configure Swagger only in Development**

Register `AddEndpointsApiExplorer` and `AddSwaggerGen`, including bearer authentication metadata. Map `UseSwagger` and `UseSwaggerUI` only inside the Development environment condition. Keep the existing built-in OpenAPI route only if tests or compatibility still require it.

- [ ] **Step 4: Run focused and complete integration tests**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj`

Expected: PASS with zero failures.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Packages.props src/Messenger.Api/Messenger.Api.csproj src/Messenger.Api/Program.cs tests/Messenger.Api.IntegrationTests/Security/HardeningTests.cs
git commit -m "feat: expose development swagger ui"
```

### Task 3: Complete Endpoint Documentation

**Files:**
- Modify: `src/Messenger.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/ProfileEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/ContactEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/ChatEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/ChatFolderEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/MessageEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/UploadEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/MusicEndpoints.cs`
- Modify: `src/Messenger.Api/Endpoints/SupportEndpoints.cs`
- Modify: `src/Messenger.Api/Program.cs`
- Modify: `tests/Messenger.Api.IntegrationTests/Security/HardeningTests.cs`

**Interfaces:**
- Produces: stable OpenAPI tags, operation identifiers, summaries, descriptions, and response metadata for every HTTP API route.
- Consumes: Minimal API `WithTags`, `WithName`, `WithSummary`, `WithDescription`, and `Produces` conventions.

- [ ] **Step 1: Write a failing generated-document completeness test**

Fetch `/swagger/v1/swagger.json`, enumerate every operation under `/api/*`, `/health`, and `/`, and assert that each has a non-empty `operationId`, `summary`, `description`, and at least one tag. Assert representative 401/403/problem response metadata on authorized operations.

- [ ] **Step 2: Run the documentation test and verify RED**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~Swagger_operations"`

Expected: FAIL listing undocumented operations.

- [ ] **Step 3: Add metadata to all endpoint mappings**

Use concise Russian descriptions visible to the project owner. Document authentication expectations and common error statuses without exposing secrets or test credentials. Describe `/hubs/chat` in the Swagger overview because WebSocket invocation is outside Swagger's execution model.

- [ ] **Step 4: Re-run the documentation test and full integration suite**

Run: `dotnet test tests/Messenger.Api.IntegrationTests/Messenger.Api.IntegrationTests.csproj`

Expected: PASS with zero undocumented operations.

- [ ] **Step 5: Commit**

```powershell
git add src/Messenger.Api/Endpoints src/Messenger.Api/Program.cs tests/Messenger.Api.IntegrationTests/Security/HardeningTests.cs
git commit -m "docs: describe API operations in swagger"
```

### Task 4: Public MAUI API Endpoint

**Files:**
- Modify: `src/Messenger.Maui/Services/ApiEndpointOptions.cs`
- Modify: `src/Messenger.Maui/MauiProgram.cs`
- Modify: `src/Messenger.Maui/Services/UploadClient.cs`
- Modify: `src/Messenger.Maui/Platforms/Android/AndroidManifest.Debug.xml`
- Modify: `tests/Messenger.Maui.Tests/Services/ApiEndpointOptionsTests.cs`
- Modify: `README.md`

**Interfaces:**
- Produces: `ApiEndpointOptions.Create(bool isAndroid, bool isDebug, string? configuredBaseAddress = null)` with default `https://api.projectdomain.ru/` and matching `ChatHubAddress`.
- Consumes: optional `MESSENGER_API_BASE_URL` runtime override for explicit local development.

- [ ] **Step 1: Replace endpoint expectations with failing public-HTTPS tests**

Assert that Android and iOS/desktop, Debug and Release, default to `https://api.projectdomain.ru/`; assert that an absolute HTTPS override is normalized and that HTTP is accepted only for the explicit Android Debug local-emulator address.

- [ ] **Step 2: Run MAUI endpoint tests and verify RED**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj --filter "FullyQualifiedName~ApiEndpointOptionsTests"`

Expected: FAIL because Debug still uses localhost/emulator HTTP and Release uses localhost HTTPS.

- [ ] **Step 3: Implement deployed endpoint selection and upload URL behavior**

Read the optional override in `MauiProgram`, reject unsafe non-local HTTP values, and remove unconditional deployed URL rewriting from `UploadClient`. Retain Android cleartext permission only for the explicit local Debug override documented in README.

- [ ] **Step 4: Run MAUI tests and build Android Debug and Release**

Run: `dotnet test tests/Messenger.Maui.Tests/Messenger.Maui.Tests.csproj`

Run: `dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Debug`

Run: `dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Release`

Expected: all commands exit 0 with no build errors.

- [ ] **Step 5: Commit**

```powershell
git add src/Messenger.Maui tests/Messenger.Maui.Tests/Services/ApiEndpointOptionsTests.cs README.md
git commit -m "feat: connect maui to deployed api"
```

### Task 5: Server Runtime and Atomic Deployment Assets

**Files:**
- Create: `deploy/docker-compose.infrastructure.yml`
- Create: `deploy/Caddyfile`
- Create: `deploy/messenger-api.service`
- Create: `deploy/messenger-deploy.sudoers`
- Create: `deploy/bootstrap-ubuntu.sh`
- Create: `deploy/deploy-release.sh`
- Create: `deploy/messenger.env.example`
- Create: `tests/deploy/deploy-assets.Tests.ps1`
- Modify: `.gitignore`
- Modify: `README.md`

**Interfaces:**
- Produces: `/usr/local/sbin/messenger-deploy <archive> <sha>`, `messenger-api.service`, `/opt/messenger/current`, loopback PostgreSQL/MinIO, and Caddy routes for both hostnames.
- Consumes: release archive containing `api/` and executable `efbundle`; `/etc/messenger/messenger.env`.

- [ ] **Step 1: Write failing static deployment-asset tests**

Assert loopback-only infrastructure ports, exact public hostnames, API and SignalR proxying, private-bucket proxying, secret-file permissions, release-name allow-listing, health-check rollback, and absence of committed real secret values.

- [ ] **Step 2: Run deployment-asset tests and verify RED**

Run: `pwsh -File tests/deploy/deploy-assets.Tests.ps1`

Expected: FAIL because deployment assets do not exist.

- [ ] **Step 3: Implement infrastructure, bootstrap, systemd, Caddy, and deployment scripts**

Make bootstrap idempotent. Generate service identities and directories without deleting unrelated server state. Validate resolved paths before release cleanup. Keep a bounded number of old releases and never remove Docker volumes.

- [ ] **Step 4: Validate assets locally and on an Ubuntu shell**

Run: `pwsh -File tests/deploy/deploy-assets.Tests.ps1`

Run on Ubuntu: `bash -n deploy/bootstrap-ubuntu.sh deploy/deploy-release.sh`

Run on Ubuntu after Docker installation: `docker compose -f deploy/docker-compose.infrastructure.yml config`

Expected: all validations exit 0 and Compose exposes no database/object-store port on a public interface.

- [ ] **Step 5: Exercise rollback with a controlled failing release**

Install a known-good test service, invoke the deploy script with a release whose health check fails, and assert that `/opt/messenger/current` resolves to the prior release and the prior service is active.

- [ ] **Step 6: Commit**

```powershell
git add deploy tests/deploy .gitignore README.md
git commit -m "feat: add ubuntu deployment runtime"
```

### Task 6: GitHub Actions CI/CD

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/deploy-development.yml`
- Create: `scripts/package-linux-release.ps1`
- Create: `tests/deploy/workflow.Tests.ps1`
- Modify: `README.md`

**Interfaces:**
- Produces: CI on pull requests/pushes and deployment on `main`/manual dispatch.
- Consumes: GitHub secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`, `DEPLOY_HOST_KEY`.

- [ ] **Step 1: Write failing workflow contract tests**

Assert pinned action major versions, least-privilege permissions, full test execution, self-contained `linux-x64` API publish, migration bundle creation, SHA-named archive, SSH host-key pinning, and no password/root login.

- [ ] **Step 2: Run workflow tests and verify RED**

Run: `pwsh -File tests/deploy/workflow.Tests.ps1`

Expected: FAIL because workflows and packaging script do not exist.

- [ ] **Step 3: Implement CI, release packaging, and deployment workflow**

The deployment job depends on successful tests. It copies one SHA-addressed archive, invokes only the restricted deployment command, and performs a public HTTPS health check. Concurrency permits only one development deployment at a time.

- [ ] **Step 4: Validate workflow syntax and release packaging**

Run: `pwsh -File tests/deploy/workflow.Tests.ps1`

Run: `pwsh -File scripts/package-linux-release.ps1 -Configuration Release -Output artifacts/deployment-test`

Expected: tests pass and the archive contains `api/Messenger.Api`, `efbundle`, and no `.env` or secret file.

- [ ] **Step 5: Commit**

```powershell
git add .github/workflows scripts/package-linux-release.ps1 tests/deploy/workflow.Tests.ps1 README.md
git commit -m "ci: deploy development api from main"
```

### Task 7: Bootstrap Ubuntu and Perform Initial Deployment

**Files:**
- Use: `deploy/bootstrap-ubuntu.sh`
- Use: `deploy/deploy-release.sh`
- Use: `scripts/package-linux-release.ps1`
- Server create: `/etc/messenger/messenger.env`
- Server create: `/opt/messenger/infrastructure/.env`
- Server create: `/home/messenger-deploy/.ssh/authorized_keys`

**Interfaces:**
- Produces: live temporary HTTPS service and a server ready for GitHub Actions.
- Consumes: root SSH access only during bootstrap, generated independent application secrets, and the first local release archive.

- [ ] **Step 1: Capture a read-only server baseline**

Record OS, disk, memory, listening ports, firewall status, and existing services. Stop if a planned path or port belongs to unrelated software.

- [ ] **Step 2: Generate deployment and application secrets without logging them**

Generate independent PostgreSQL, MinIO, encryption, blind-index, challenge-hash, JWT, and deploy SSH credentials. Transfer them through protected stdin/files, set server permissions to 600, and run the repository secret scanner before continuing.

- [ ] **Step 3: Run the idempotent bootstrap and validate service isolation**

Install Docker, Caddy, and required utilities; create identities and paths; enable UFW; start PostgreSQL and MinIO. Verify public listening ports remain limited to 22, 80, and 443.

- [ ] **Step 4: Package and deploy the first release**

Run the local packaging script, upload the SHA archive, invoke `/usr/local/sbin/messenger-deploy`, and verify systemd reports `messenger-api.service` active.

- [ ] **Step 5: Verify temporary public HTTPS behavior**

Check `/health`, `/swagger`, `/swagger/v1/swagger.json`, allowed/rejected CORS preflight, auth challenge, SignalR negotiation, and presigned upload reachability through `https://185-56-162-174.sslip.io`.

- [ ] **Step 6: Verify and activate the primary domain when DNS resolves**

Require `api.projectdomain.ru A 185.56.162.174` from authoritative and public resolvers, then verify Caddy has issued a trusted certificate and repeat the public smoke checks on `https://api.projectdomain.ru`.

### Task 8: Configure Repository Deployment Secrets and Prove CI/CD

**Files:**
- Use: `.github/workflows/deploy-development.yml`
- Use: server deploy public key and SSH host key generated/captured in Task 7.

**Interfaces:**
- Produces: a GitHub repository able to deploy `main` without root credentials.
- Consumes: authenticated access to `holo2k/MessengerMAUI` repository settings.

- [ ] **Step 1: Add repository deployment secrets through authenticated GitHub access**

Set `DEPLOY_HOST=185.56.162.174`, `DEPLOY_USER=messenger-deploy`, the generated private key, and the exact pinned server host-key line. Never print secret values after creation.

- [ ] **Step 2: Push only MessengerMAUI commits after explicit repository verification**

Verify `origin` is exactly `https://github.com/holo2k/MessengerMAUI.git`, show the outgoing commit list, and push `main`. Do not access or push any M.Video repository.

- [ ] **Step 3: Observe the workflow and verify deployment evidence**

Require successful test and deploy jobs, confirm the server's active release SHA matches GitHub `main`, and repeat the HTTPS health and Swagger checks.

- [ ] **Step 4: Run the complete local verification suite**

Run: `pwsh -File scripts/verify.ps1`

Run: `dotnet test Messenger.slnx --no-restore`

Run: `dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Release`

Expected: all commands exit 0 with zero test failures and zero build errors.

- [ ] **Step 5: Record operational handoff**

Report public URLs, active commit SHA, service status, workflow result, DNS state, rollback command, credential-rotation reminder, and any verification that remains blocked by DNS propagation.
