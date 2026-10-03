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
2. Запустите зависимости: `docker compose up -d postgres minio`.
3. Примените миграции: `dotnet ef database update --project src/Messenger.Infrastructure --startup-project src/Messenger.Infrastructure`.
4. Запустите API: `./scripts/run-api.ps1`. Скрипт безопасно импортирует `.env` и согласует ASP.NET connection string/MinIO credentials с Compose; сам `dotnet run` файл `.env` не читает.
5. Запустите Android: `dotnet build src/Messenger.Maui -f net10.0-android -t:Run`.

На Windows AAPT2 не принимает кириллицу в пути. Создайте один раз ASCII junction и выполняйте Android-команды из него:

```powershell
New-Item -ItemType Junction -Path "$env:LOCALAPPDATA\MessengerWorkspace" -Target (Get-Location)
Set-Location "$env:LOCALAPPDATA\MessengerWorkspace"
```

Для локального HTTPS выполните `dotnet dev-certs https --trust`. Android-эмулятор обращается к хосту как `10.0.2.2`. Cleartext разрешён только этому адресу в Debug; Release требует HTTPS.

## Проверка

Из PowerShell выполните `./scripts/verify.ps1`. Скрипт проверяет форматирование, restore, все тесты, API, Android, миграции, здоровье Compose и возможные секреты. iOS собирается только на macOS — см. [docs/IOS-BUILD.md](docs/IOS-BUILD.md).

Подробности: [API](docs/API.md), [безопасность](docs/SECURITY.md), [музыка и права](docs/MUSIC-COPYRIGHT.md).
