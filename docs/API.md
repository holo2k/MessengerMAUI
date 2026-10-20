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
- Музыка: search/library/tracks/declaration/claims/stream/download, изменение обложки `/music/tracks/{id}/cover`; moderation под `/admin/music`.
- Поддержка: `/support/tickets`; staff endpoints `/admin/support/tickets`.
- Удаление аккаунта: `POST|DELETE /me/deletion`.
- Диагностика: `GET /health`.

OpenAPI доступен только в Development по `/openapi/v1.json`. Для экспорта при запущенном API:

```powershell
New-Item -ItemType Directory -Force artifacts | Out-Null
Invoke-WebRequest https://localhost:7106/openapi/v1.json -OutFile artifacts/openapi.json
```

## Обложки и песни в чатах

При создании трека `coverObjectId` может ссылаться на собственное загруженное изображение JPEG или PNG. Загрузивший трек пользователь может заменить обложку: `PUT /api/music/tracks/{id}/cover` с телом `{ "coverObjectId": "<uuid>" }`. Метод возвращает обновлённый `MusicTrackResponse`; поле `uploaderUserId` позволяет показать редактирование только владельцу. Обложки доступных треков доступны авторизованным слушателям через `GET /api/objects/{coverObjectId}/download`.

Для отправки песни используйте `POST /api/chats/{chatId}/messages`:

```json
{
  "clientMessageId": "<uuid для повторных попыток>",
  "type": "music",
  "body": null,
  "musicTrackId": "<uuid доступного трека>"
}
```

Отправитель должен состоять в чате. Аудиофайл остаётся у трека; сообщение хранит ссылку на него и зашифрованную подпись. В ответах истории, поиска и закреплённых сообщений поле `musicTrack` содержит актуальные метаданные и обложку; у удалённых сообщений и недоступных треков это поле равно `null`. Обычное событие `MessageCreated` сообщает о новой песне в чате, а `MusicTrackUpdated` с полем `track` обновляет обложку в открытых клиентах.

Изменение добавляет миграцию `20261021000100_SharedMusicMessages`. При стандартном деплое её применяет EF migration bundle; API и приложение нужно обновить вместе.

## Коды

Используются `401 unauthorized`, `403 forbidden`, `404 *_not_found`, `409 conflict`/доменные конфликты, `422` для нарушений правил и валидации, `429 too_many_requests`, `503 email_delivery_unavailable`.
