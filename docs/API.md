# API MVP

Базовый путь — `/api`; защищённые методы принимают `Authorization: Bearer <access-token>`. Realtime hub: `/hubs/chat`. Ошибки имеют `application/problem+json`, стабильное поле `code`, `correlationId` и заголовок `X-Correlation-ID`.

## Маршруты

- Auth: `POST /auth/challenges`, `/auth/register`, `/auth/login`, `/auth/2fa/confirm`, `/auth/refresh`, `/auth/logout`, `/auth/logout-all`.
- Профиль: `GET|PATCH /me`, avatar, privacy, sessions, email 2FA и phone change под `/me/*`.
- Контакты: поиск и CRUD под `/contacts`.
- Чаты: `GET /chats`, `POST /chats/direct`, `POST /chats/groups`, membership/roles, archive/mute/read и group editing.
- Папки: CRUD, порядок и состав под `/chat-folders`.
- Сообщения: list/send/search под `/chats/{id}/messages`; edit/delete/pin под `/messages/{id}`.
- Медиа: upload session/complete, авторизованная загрузка объекта, media list чата.
- Музыка: search/library/tracks/declaration/claims/stream/download; moderation под `/admin/music`.
- Поддержка: `/support/tickets`; staff endpoints `/admin/support/tickets`.
- Удаление аккаунта: `POST|DELETE /me/deletion`.
- Диагностика: `GET /health`.

OpenAPI доступен только в Development по `/openapi/v1.json`. Для экспорта при запущенном API:

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
Invoke-WebRequest https://localhost:7106/openapi/v1.json -OutFile artifacts/openapi.json
```

## Коды

Используются `401 unauthorized`, `403 forbidden`, `404 *_not_found`, `409 conflict`/доменные конфликты, `422` для нарушений правил и валидации, `429 too_many_requests`, `503 email_delivery_unavailable`.
