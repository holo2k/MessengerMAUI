# Сборка iOS

iOS target включается в `Messenger.Maui.csproj` только на macOS. Нужны актуальные Xcode, .NET SDK 10.0.401+, workload `maui-ios`, Apple signing identity и provisioning profile для `com.companyname.messenger.maui`.

```bash
dotnet workload restore
dotnet restore Messenger.slnx
dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
```

Затем запустите приложение в Simulator из Visual Studio/CLI, проверьте login, SecureStorage, camera/photo permissions, microphone, file picker, SignalR reconnect, background/foreground player lifecycle и открытие номера через системный dialer.

ATS запрещает произвольный HTTP. Для устройства API обязан иметь доверенный HTTPS-сертификат; localhost host Mac недоступен как localhost с физического iPhone. В текущей Windows-среде iOS-сборка не проверялась и не считается прошедшей.
