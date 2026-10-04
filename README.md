# Messenger MVP

Рабочий MVP мессенджера для Android/iOS: клиент .NET MAUI, API ASP.NET Core, PostgreSQL, MinIO и SignalR. Интерфейс намеренно минимальный — он показывает серверные сценарии, а не является финальным дизайном.

## Что реализовано

- регистрация и вход по российскому номеру; в Development SMS-код фиксирован: `111111`;
- JWT access token, ротация refresh token, отзыв сессий и защита от повторного использования;
- профиль, username, био, аватар, приватность, смена номера и email 2FA;
- контакты, прямые и групповые чаты, роли, архив, прочтение, mute и папки с изменяемым порядком;
- шифрование текста сообщений на сервере, поиск, редактирование, удаление, закрепление и SignalR-синхронизация;
- приватные загрузки MinIO, фото/видео/файлы/аудиосообщения;
- музыкальная библиотека, загрузка с декларацией прав, модерация, streaming/download и локальный MAUI-плеер;
- поддержка и отложенное на 30 дней удаление аккаунта.

## Быстрый запуск

Требуются .NET SDK 10.0.401+, workload MAUI/Android, Docker Desktop, Android SDK и JDK 21.

1. Скопируйте `.env.example` в `.env` и замените все значения `replace-with-*`. Каждый криптографический ключ должен быть независимым случайным Base64-значением на 32 байта. SMTP-пароль храните только в `.env` или Secret Manager.
2. Подготовьте зависимости и БД: `./scripts/prepare-dev.ps1`. Скрипт запускает PostgreSQL/MinIO, ждёт healthcheck, загружает параметры именно из `.env` и применяет миграции с коротким таймаутом подключения.
3. В отдельном PowerShell запустите API: `./scripts/run-api.ps1`.
4. Ещё в одном PowerShell запустите Android: `./scripts/run-android.ps1`. Скрипт сам найдёт установленный AVD, запустит эмулятор, дождётся полной загрузки и развернёт Debug-приложение. Конкретный AVD можно выбрать через `-AvdName pixel_7_-_api_36_0`.

Не запускайте для локальной разработки голые команды `dotnet ef database update` и `dotnet build -t:Run`: первая не загружает секреты PostgreSQL из `.env`, а вторая не запускает выключенный эмулятор. По умолчанию все сборки MAUI обращаются к общему Development API по `https://api.projectdomain.ru/`.

`run-api.ps1` намеренно не завершается, пока работает сервер. Оставьте это окно открытым; строка `Now listening on: http://localhost:5192` означает успешный запуск. Остановка — `Ctrl+C`. В отличие от него, `prepare-dev.ps1` должен завершиться сообщением `Development dependencies and database are ready.`.

На Windows AAPT2 не принимает кириллицу в пути. Текущий каталог `MessengerMAUI` уже содержит только ASCII, поэтому junction не нужен. Если проект позднее будет перемещён в путь с кириллицей, создайте ASCII junction:

```powershell
New-Item -ItemType Junction -Path "$env:LOCALAPPDATA\MessengerWorkspace" -Target (Get-Location)
Set-Location "$env:LOCALAPPDATA\MessengerWorkspace"
```

Для явного локального запуска задайте `MESSENGER_API_BASE_URL=http://10.0.2.2:5192/` перед запуском Android Debug. Cleartext разрешён только адресу эмулятора `10.0.2.2` в Debug; Release и любые удалённые адреса требуют HTTPS.

## Проверка

Из PowerShell выполните `./scripts/verify.ps1`. Скрипт проверяет форматирование, restore, все тесты, API, Android, миграции, здоровье Compose и возможные секреты. iOS собирается только на macOS — см. [docs/IOS-BUILD.md](docs/IOS-BUILD.md).

Подробности: [API](docs/API.md), [безопасность](docs/SECURITY.md), [музыка и права](docs/MUSIC-COPYRIGHT.md).

## Development deployment

Ubuntu-конфигурация находится в `deploy/`: Caddy публикует API и Swagger по HTTPS, ASP.NET работает как systemd-сервис, а PostgreSQL и MinIO доступны только через loopback. Первый запуск выполняется `bootstrap-ubuntu.sh`, последующие SHA-релизы устанавливаются скриптом `messenger-deploy`. Секреты создаются непосредственно на сервере по шаблонам `messenger.env.example` и `infrastructure.env.example` и никогда не добавляются в Git.

GitHub Actions запускает полную проверку изменений, а push в `main` дополнительно собирает self-contained Linux-релиз, EF migration bundle и устанавливает SHA-релиз через пользователя `messenger-deploy`. Репозиторию нужны secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY` и `DEPLOY_HOST_KEY`.
