# Development Deployment Design

## Goal

Deploy the Messenger ASP.NET API to the Ubuntu server at `185.56.162.174` as a repeatable development environment, expose it through HTTPS at `api.projectdomain.ru`, provide an interactive Swagger UI with endpoint descriptions, and configure the MAUI Android/iOS client to use the deployed API and SignalR hub.

## Scope

This design covers:

- the initial Ubuntu server bootstrap;
- PostgreSQL and MinIO infrastructure;
- ASP.NET API publishing and database migrations;
- Caddy reverse proxy and automatic TLS;
- Development Swagger UI and OpenAPI metadata;
- configurable CORS;
- MAUI API, SignalR, and upload endpoints;
- GitHub Actions CI/CD for `main` and manual runs;
- deployment verification and rollback.

It does not introduce production SMS delivery, push notifications, E2E encryption, a web frontend, or a production-hardening environment. The deployed API intentionally uses `ASPNETCORE_ENVIRONMENT=Development` so the requested interactive Swagger interface is available.

## Public Addresses

- Primary API: `https://api.projectdomain.ru/`
- Swagger UI: `https://api.projectdomain.ru/swagger`
- OpenAPI document: `https://api.projectdomain.ru/swagger/v1/swagger.json`
- SignalR hub: `https://api.projectdomain.ru/hubs/chat`
- Temporary verification address while DNS propagates: `https://185-56-162-174.sslip.io/`

Caddy serves both hostnames. The primary hostname becomes usable automatically after its A record resolves to `185.56.162.174`. Caddy obtains and renews separate publicly trusted certificates for each hostname.

## Server Architecture

Caddy is the only public application service. It listens on ports 80 and 443, redirects HTTP to HTTPS, terminates TLS, and reverse-proxies HTTP and WebSocket traffic to the API on `127.0.0.1:5192`. Requests under the private bucket path `/messenger-private/*` are routed to MinIO on its loopback port so presigned uploads use the same public HTTPS hostname without exposing the MinIO console.

The API runs as a systemd service from `/opt/messenger/current`. Each deployment is stored in `/opt/messenger/releases/<git-sha>`, and `current` is an atomic symbolic link to the active release. The API is published as a self-contained `linux-x64` application, so the server does not require a .NET SDK or runtime.

PostgreSQL and MinIO run under Docker Compose. Their ports bind only to loopback and are not exposed by the firewall. Persistent Docker volumes retain database and object data across application deployments.

Application secrets and connection settings live in `/etc/messenger/messenger.env`, owned by root and readable by the application service group only. Secrets never enter Git, build artifacts, command output, or GitHub Actions logs.

## Server Identity and Access

Initial bootstrap uses the supplied root access. Ongoing CI/CD uses a dedicated `messenger-deploy` account authenticated by an SSH key.

The deploy account receives narrowly scoped `sudo` permissions for the deployment script and the Messenger systemd service. It cannot open an unrestricted root shell through the CI/CD workflow. Root password authentication should be disabled after SSH-key access is verified, and the root password shared in chat should be rotated.

UFW permits only:

- TCP 22 for SSH;
- TCP 80 for ACME validation and HTTPS redirect;
- TCP 443 for the API, Swagger, uploads, and SignalR.

## API Configuration

The API adds a named CORS policy populated from `Cors:AllowedOrigins`. The policy allows configured origins, required HTTP methods and headers, and credentials for SignalR. It does not use `AllowAnyOrigin` together with credentials. Native MAUI requests do not depend on CORS, but the policy supports Swagger and a future trusted web client.

Forwarded headers trust only the local Caddy proxy. ASP.NET derives the original HTTPS scheme from Caddy and does not expose Kestrel directly.

Development SMS remains the configured provider. Authentication challenge responses therefore keep the existing development-code behavior. SMTP, encryption, blind-index, challenge-hash, JWT, PostgreSQL, and MinIO values are supplied through the protected server environment file.

## Swagger and Endpoint Documentation

The API uses Swashbuckle to generate an OpenAPI v3 document and host Swagger UI at `/swagger`. Swagger is enabled only when the ASP.NET environment is Development.

Every mapped API endpoint and the health endpoint receive stable OpenAPI operation metadata:

- a concise summary;
- a description of purpose and authentication requirements;
- response metadata for the normal result and relevant error statuses;
- tags that group authentication, profile, contacts, chats, folders, messages, uploads, music, support, account, realtime, and diagnostics operations.

DTO XML comments are included where they materially clarify fields. Sensitive values and server secrets never appear as Swagger examples or defaults. The SignalR WebSocket protocol is documented by describing the hub endpoint and events; Swagger itself does not execute SignalR sessions.

## MAUI Endpoint Selection

Release builds use `https://api.projectdomain.ru/` for REST, SignalR, and upload-session creation. Debug builds also use the deployed HTTPS API by default so emulator/device behavior matches the shared development server.

A compile-time MSBuild property can override the API base address for local development. The generated value is supplied to `ApiEndpointOptions`; hard-coded emulator and localhost addresses are removed from production selection logic. Android remains cleartext-disabled for deployed builds.

MinIO is configured with the public API hostname when generating presigned URLs. Caddy recognizes the `/messenger-private/*` bucket path and forwards only those object requests to MinIO. MAUI therefore no longer rewrites deployed upload URLs to `10.0.2.2`; local-only rewriting remains limited to an explicit local-development override.

## CI/CD Pipeline

GitHub Actions runs on pushes to `main` and through `workflow_dispatch`.

The pipeline performs these stages in order:

1. Restore dependencies.
2. Run the complete automated test suite.
3. Publish the API as self-contained `linux-x64` output.
4. Build a self-contained EF Core migration bundle.
5. Package the release using the current commit SHA.
6. Copy the package to the server with the dedicated deployment SSH key.
7. Invoke the restricted server deployment script.
8. Run migrations against the server database.
9. Atomically switch `/opt/messenger/current` and restart `messenger-api.service`.
10. Verify the local health endpoint and the public HTTPS health endpoint.

GitHub repository secrets contain only deployment connection material:

- `DEPLOY_HOST`;
- `DEPLOY_USER`;
- `DEPLOY_SSH_KEY`;
- `DEPLOY_HOST_KEY`.

Application secrets remain server-side. Host-key pinning is mandatory; the workflow does not disable SSH host verification.

The initial deployment can be performed from the local workstation with the same release layout and server deployment script. Later releases use the workflow without repeating bootstrap operations.

## Deployment and Rollback

The server deployment script validates the archive name and target path, extracts into a new release directory, runs the migration bundle, and only then switches the `current` link. It restarts the service and waits for `http://127.0.0.1:5192/health`.

If extraction or migration fails, the active release is untouched. If the service fails after switching, the script restores the previous link, restarts the previous release, and exits non-zero. A small fixed number of old releases is retained; database and MinIO data are never deleted by application deployment.

Database migrations must be backward-compatible with the immediately preceding application release because application rollback does not automatically reverse schema changes.

## Verification

Automated tests cover:

- the deployed MAUI endpoint and SignalR address;
- local endpoint override behavior;
- allowed and rejected CORS preflight requests;
- Swagger JSON and Swagger UI availability in Development;
- Swagger absence outside Development;
- endpoint operation summaries and descriptions;
- forwarded HTTPS behavior.

Deployment verification checks:

- only ports 22, 80, and 443 are publicly reachable;
- `/health` returns HTTP 200 locally and through HTTPS;
- `/swagger` loads its referenced OpenAPI document;
- an allowed CORS preflight returns the configured origin;
- an unlisted origin receives no CORS allow-origin header;
- an authentication challenge request succeeds;
- a SignalR negotiation request reaches the hub;
- an upload session returns a publicly reachable HTTPS URL;
- Android can request a development login code through the deployed API.

## Operational Notes

The requested deployment is a shared development environment. Swagger and development authentication behavior are intentionally public and must not be treated as production-ready. Before a production launch, the project needs a Production environment, real SMS verification, restricted API documentation, monitoring and backups, secret rotation, and a separate production deployment pipeline.
