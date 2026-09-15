# Android: локальный серверный матч
Status: Android ARM64 IL2CPP USB lifecycle and full match verified; radio-network and touch QA pending
Last reviewed: 2026-09-13

## Стенд

Отдельный development APK `com.tankdraft.localqa` (ARM64, IL2CPP) подключается через `adb reverse tcp:18783 tcp:18783` к standalone серверу на ПК. Сервер слушает только loopback; HTTPS login и WSS используют существующий scoped TLS pin. Второе место занимает настоящий Windows Unity Player с автоматическим выбором. PlayFab/Azure/MPS не вызываются, firewall и системное хранилище сертификатов не меняются.

Сборка через открытый Editor/MCP: `TankDraft.Match.ServerClient.Editor.ServerClientAndroidBuild.Queue()`. Сохраняет исходные Android settings и target в SessionState, временно включает `TANKDRAFT_LOCAL_QA`, isolated package ID, development/debug, Internet permission, ARM64 IL2CPP. После сборки восстанавливает настройки и target. APK: `Logs/BackendAndroid/Build/TankDraftLocalQA.apk`; статус: `Logs/BackendAndroid/build-status.txt`. Не запускать batchmode рядом с Editor.

## Одноразовый доступ

`LocalAndroidQaBootstrap` компилируется только для Android development QA. Launcher передаёт JSON Grant/Pin/RunId/Auto через stdin ADB в app-private `files/td-bootstrap.json`; credentials не включены в APK, аргументы запуска или файлы ПК. Приложение читает ограниченный файл и строго проверяет поля. В legacy single-match режиме валидный bootstrap удаляется после чтения; после force-stop launcher передаёт его снова. В queue-режиме дополнительное поле Queue=true сохраняет bootstrap в приватной папке на время локального серверного сеанса, чтобы обычный повторный запуск восстановил очередь/матч. При завершении launcher удаляет bootstrap. Диагностика и intent journal остаются в private `files/qa/<RunId>` и не содержат grant/access token. Grant ограничен сроком локального прогона и отзывается при остановке host. Это устройство для разработки с ADB/run-as, не production login.

## Жизненный цикл

Authored `ServerClientScope` передаёт Android pause/resume существующей session. При pause transport закрывает socket, перестаёт получать состояние и принимать новые UI-команды, сохраняет pending intent. При возврате получает доступ и полный актуальный snapshot; presentation buffer сбрасывается. Сервер продолжает считать бой и обрабатывать дедлайны независимо от Android процесса.

## Запуск и проверка

После установки APK на разрешённый ADB device:

```powershell
./Tools/Backend/run-android-client.ps1 -Serial '<adb serial>' -Automated
./Tools/Backend/verify-android-client-run.ps1 -RunDirectory 'Logs/BackendClient/android-<run id>'
```

Автоматический прогон проверяет Battle, HOME на 12 секунд, возврат к более свежему боевому состоянию, удаление USB tunnel и force-stop на 40 секунд, повторный запуск с тем же journal, MatchResult. Launcher собирает private evidence, записывает серверные snapshots вокруг offline, закрывает собственные процессы и удаляет только созданный им reverse. Уже занятый reverse port отвергается. APK остаётся установленным. Без `-Automated` выбор на телефоне ручной, соперник автоматический; локальный host ограничен 850 секундами.

Проверка сравнивает полные хэши сущностей на общих тиках, финальный счёт/revision, stable StreamId, смену access generation, отсутствие pending команд, pause/resume callbacks, progression сервера и screenshots. Результат запуска ADB не считается exit code Android Player.

Проверки кода: `test-client-protocol.ps1` — 37/37, включая отсутствие login до resume и корректный dispose. QA define передаётся через BuildPlayerOptions.extraScriptingDefines без изменения общих compilation flags Editor.

APK установлен через `adb install --no-streaming -r -t`: Succeeded, 0 errors, 3 warnings, 214751305 bytes. Исходные target StandaloneWindows64, package ID и define symbols восстановлены; сцена чистая, Edit Mode. Остаются предупреждения настройки input/localization; полноценный touch QA и подготовка release metadata отдельно.

На Xiaomi 11 Lite 5G NE (Android 14, ARM64) завершён реальный Windows + Android матч:

- Evidence: `Logs/BackendClient/android-cebbedfe5a79056817ff436ff9910a96`.
- `verify-android-client-run.ps1`: PASS, 7 раундов, счёт сервера 4:3 (на телефоне 3:4), revision 2746.
- 129 общих боевых тиков, 0 несовпадений хэшей полного состояния сущностей.
- HOME/foreground: реальные OnApplicationPause true/false; возврат из первого во второй раунд.
- Force-stop + удалённый ADB reverse: 41 секунда отсутствия; серверная revision выросла 260 → 498, раунд 2 → 3. После повторного запуска клиент догнал актуальный матч.
- Access generation Android до 5, stable StreamId, pending intent отсутствует. Windows завершился с code 0; launcher Completed=true и exit 0.
- В private storage сохранены 7 battle PNG и result PNG. Скриншоты боя и результата просмотрены. Логи двух Android процессов собраны, Unity errors/FATAL не найдены.
- После автоматического прогона собственный reverse 18783 и listener закрыты, APK сохранён на устройстве.

Исправления, подтверждённые устройством: stdin передаётся через `adb shell -T` (exec-out предназначен для вывода), UTF-8 без BOM; перед стартом проверяется наличие непустого private bootstrap. HOME вызывается через Activity Manager без имитации нажатий. Android screenshots снимаются в конце кадра через CaptureScreenshotAsTexture и записываются в private directory; CaptureScreenshot с абсолютным путём на Android ошибочно добавлял persistentDataPath.

В кадре на границе Draft/Battle возможен краткий старый текст HUD до следующего обновления (период 0.1 секунды); это остаётся задачей визуальной полировки, не расхождение серверной симуляции. Автоматический выбор не подтверждает ручной touch input.

USB tunnel проверяет Android runtime/lifecycle и реальный разрыв TCP. Он не проверяет переключение Wi-Fi/LTE, мобильную радиосеть, production PlayFab identity или MPS. Эти проверки остаются отдельными.

## Очередь из главного меню

Для нового menu-first APK используется ServerClientAndroidBuild.QueueMenu(); Windows peer собирается через ServerClientBuild.QueueMenu(). Варианты android-pair/android-solo и ручной поиск описаны в [Matchmaking_and_Bots.md](Matchmaking_and_Bots.md). В paired режиме оба клиента управляются вручную, если не задан -Automated.
