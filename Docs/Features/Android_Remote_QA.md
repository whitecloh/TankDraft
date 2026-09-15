# Android: закрытый тест PlayFab / Edgegap
Status: VPN ON full PC/Android Human match with parallel renewal PASS; repeated matches, faults and load pending
Last reviewed: 2026-09-14

Актуальный run `fe5f3b821b37`: 5 s draft, один общий Human match PC/Android, финал0:4, 88 общих Battle ticks без hash mismatch, один connection на каждом. Android median109 ms/p95114 ms/max155 ms; владелец подтвердил заметную плавность без восстановления связи. PC max433 ms остаётся отдельным наблюдением. Полные evidence/границы: [VPN_QA_Stabilization.md](VPN_QA_Stabilization.md#human-vpn-on-с-пятисекундным-ходом-fe5f3b821b37).

Новое условие владельца: дальнейшая приёмка проходит с VPN ON. Исторические OFF/ON результаты ниже сохраняются. Текущий план и границы: [VPN_QA_Stabilization.md](VPN_QA_Stabilization.md). Run `644b29b074b9`: Android и PC завершили разные server-Bot матчи 4:2 на одном соединении каждый; Android median cadence 109 ms, p95 112 ms, max 887 ms при renewal. Это не общий human match. Deployment Terminated, private bootstraps удалены. Последующая доработка parallel renewal прошла ServerClient 76/76 и RemoteHost 30/30, требует новой device проверки.

Сборка 14.09.2026 ожидала в `BuildPipeline.BuildPlayer` диалог Unity о системе ввода: владелец подтвердил его, после чего запустился IL2CPP. `RUNNING` без compiler subprocess в этом случае не означал сетевое зависание приложения. При повторении проверить окно Editor; не перезапускать его и не менять input backend вслепую.

## Граница

Отдельный development APK `TankDraft Remote QA`, пакет `com.tankdraft.remoteqa`, ARM64 IL2CPP. Локальный `com.tankdraft.localqa` сохраняется. Серверный PlayFab key, Edgegap registry/control-plane credentials и CustomId не встраиваются в APK. Production authentication/discovery этим тестовым входом не реализованы.

`ServerClientAndroidBuild.QueueRemoteMenu()` вызывается через открытый Editor/MCP. Выход: `Logs/BackendAndroid/Build/TankDraftRemoteQA.apk`, статус: `Logs/BackendAndroid/build-status.txt`. Build использует `TANKDRAFT_REMOTE_ANDROID_QA`, без `TANKDRAFT_LOCAL_QA`, и восстанавливает исходные PlayerSettings/target. Remote-only generated manifest запрещает backup; исходный manifest проекта не меняется.

## Тестовые настройки

Android bootstrap доступен только в remote QA build с `Debug.isDebugBuild`. Он читает `getNoBackupFilesDir()/td-remote-bootstrap.json`; диагностические файлы — соседняя папка `remote-evidence`. Эти данные не помещаются в публичную external storage. JSON содержит только `TitleId`, `CustomId`, `BaseUri`, `ContentVersion`. Для теста разрешены title `B16D9`, заранее созданный test CustomId и HTTPS origin вида `<deployment-id>.pr.edgegap.net` с портом платформы. Query/userinfo/другие hosts отклоняются. SDK login использует `CreateAccount=false`.

Отсутствие/ошибка bootstrap оставляет remote mode заблокированным и не разрешает скрытый переход на local matchmaking. До появления активного сервера приложение показывает ожидание настройки; это не доказательство игрового соединения.

`Tools/Backend/set-remote-android-bootstrap.ps1 -Serial <device> -Endpoint <https-origin>` предназначен для передачи существующей тестовой identity через ADB stdin в закрытую папку отдельного QA-приложения. Default IdentityIndex=1; Windows peer использует identity 0. Не передавать ключи/CustomId в аргументах команд, чат, screenshot или logcat.

QA-сборка поддерживает диагностический auto-start: Android intent boolean `tankdraft.remoteAutoQueue` читается только при `UNITY_ANDROID && TANKDRAFT_REMOTE_ANDROID_QA` в debug build. После успешного Load он включает `TD_REMOTE_AUTO_QUEUE`; при обычном запуске значение `false`. Auto-start не включает автодрафт. Запуск для QA: `am start -n com.tankdraft.remoteqa/com.unity3d.player.UnityPlayerGameActivity --ez tankdraft.remoteAutoQueue true`. Автоматический вход проверен в общем матче ниже. Финальная версия потребляет флаг один раз перед автоматическим поиском.

## Порядок проверки

1. Сборка через MCP, проверка APK package/debug/manifest и установка через ADB без удаления локального прототипа.
2. Ограниченный Free Tier deployment с актуальным secret; HTTPS readiness и отказ plaintext до передачи identity.
3. Private bootstrap для телефона, Windows peer, запуск приложения и общий human match. Телефон использует мобильную сеть, ПК — отдельный доступ в интернет; игровой `adb reverse tcp:18783` недопустим. Unity debugging reverse ports не являются игровым транспортом и не удаляются попутно.
4. Сопоставление server assignment, результата, общих боевых ticks, access renewal; затем отдельные lifecycle/fault/bot проверки.
5. Остановка deployment и удаление только тестового bootstrap через `run-as` после окончания тестов. Evidence сохраняется без credential-файла.

## Проверенное на этапе подготовки APK

- Перед сборкой ADB: устройство authorized; Wi-Fi setting=0, mobile_data=1, reverse tcp:18783 отсутствует. Это проверка настроек, не доказательство маршрута игрового трафика.
- Через MCP прошли 7 проверок Android bootstrap: допустимый HTTPS Edgegap origin принимается; HTTP, сторонний host, query, userinfo, нестандартный path и duplicate JSON keys отклоняются. Fake identity, запросы PlayFab не выполнялись.
- `QueueRemoteMenu()` завершён через открытый Editor: `Succeeded`, errors=0, warnings=5. MCP подтвердил восстановление `StandaloneWindows64`, product `TankDraft`, отсутствие compilation/update. Предупреждения: Active Input Handling=Both, obsolete API в PlayFab SDK, старый PhotonServerSettings без доступного script, отсутствующие Localization Settings / Android App Info. Они требуют отдельной очистки; сборка не считается warning-free.
- Первая сборка APK проверена через `aapt`: `com.tankdraft.remoteqa`, `arm64-v8a`, debuggable=true, INTERNET, allowBackup=false, fullBackupContent=false. SHA-256 первой сборки: `94a158f37dc92815f9bed77f2533ffedd7356e12385ce51a43232abdfbafcadf`. Выходной путь позднее перезаписан обновлённой QA-сборкой.
- ADB install завершён `Success`; оба пакета remoteqa/localqa присутствуют. Приложение запущено, главное меню проверено по screenshot. В app-only logcat есть сообщения Android graphics buffer / frame insertion при старте; приложение остаётся живым, это не проверка игровой производительности.
- Реальный ADB stdin transfer проверен на 33-байтовом несекретном fixture: app-private `no_backup`, mode=600; fixture удалён. Identity/bootstrap на телефон ещё не передавались. Нажатие через ADB запрещено устройством (`INJECT_EVENTS`). После ручного нажатия «В БОЙ» владелец подтвердил: «нет связи с сервером, удаленный тест еще не настроен, ожидается адрес тестового сервера». Это ожидаемая блокировка до настройки endpoint; локальный бой не подставлен.
- Evidence: `Logs/RemoteAndroid/device-preparation-20260914/receipt.json`, `startup.png`, `startup-logcat.txt`, `build-warnings.txt` (ignored). Edgegap deployment в этом шаге не запускался.
- На этапе подготовки APK remote match / TLS / PlayFab login не проверялись; последующие cloud evidence приведены ниже. Независимые сети и lifecycle остаются отдельными критериями.

## Диагностический remote запуск 0c00d3e1d46c (2026-09-14)

Для диагностики создан ограниченный Edgegap Free Tier deployment: 0.5 vCPU, 1 GiB и максимальное время 15 минут. HTTPS readiness вернул `200`; content совпал, `economy=false`. Plaintext HTTP вернул `400`.

Телефон запускался с LTE при Wi-Fi=0. DNS разрешил endpoint в `198.58.109.76`, но приложение на этапе Join показало `Stage=ready` и цепочку `HttpRequestException` / `WebException` / `SocketException`; `HasInstance=false`, `HasLobby=false`. Отдельный Toybox TCP probe к `:30632` вне Unity также получил `Connection refused`. После включения Wi-Fi тот же TCP probe достиг endpoint, а plaintext HTTP probe вернул `400`.

Эти наблюдения не устанавливают точную причину мобильного отказа и не являются human acceptance. Windows peer `f9d5d74e528d4f1c967f15b5fef5694d` получил Bot; прогон остановлен до финала, тестовый bootstrap удалён. UI подтвердил `Terminated` для первого deployment; receipt обновлён.

Evidence: `Logs/RemoteAndroid/cloud-0c00d3e1d46c/cloud-test.json` и `Logs/RemoteAndroid/cloud-0c00d3e1d46c/android-mobile-queue-diagnostic.json` (ignored).

## Диагностический remote запуск 1455329a9d8a (2026-09-14)

UI подтвердил `Terminated` для deployment `1455329a9d8a`. Android по Wi-Fi прошёл login, assignment и presentation Battle, но получил Bot: ручной tap через чат не позволил Windows peer попасть в 10-секундное окно. Windows run: `a8d3daaab78d4182a232162573067f5a`.

Это не human acceptance. Оба клиента остановлены до финала, obsolete bootstrap удалены. Evidence: `Logs/RemoteAndroid/cloud-1455329a9d8a/cloud-test.json` и `Logs/RemoteAndroid/cloud-1455329a9d8a/android/` (ignored).

## Пересборка QA auto-start

APK auto-start собран через MCP и установлен: errors=0, warnings=3; исходный `StandaloneWindows64` восстановлен. `aapt` повторно подтвердил package, ARM64, debug и оба backup=false. SHA-256: `5ca6aadc2e9677564b1c2a8bfc13dc46b04e6631ce1e45a75f95c6b7973b0b92`. Receipt: `Logs/RemoteAndroid/device-preparation-20260914/autoqueue-build.json`. Реальное включение auto-queue подтверждено совместным запуском ниже.

## Общий матч 70537c7dc3e6 (2026-09-14)

После установки QA auto-start владелец запустил готовый скрипт пары: автоматическая проверка разрешений отклоняла совместную команду даже после явного подтверждения. Android и Windows автоматически вошли в одну очередь. Оба получили `OpponentKind=Human`, один MatchId `d3473e0564394f359dbae64149e5c680-b97e8b15ecf141f4aca727c241031195`, стороны 0/1. Android использовал Wi-Fi, игровой adb reverse отсутствовал. По уточнению владельца, Wi-Fi работал с включённым VPN (Москва, Билайн); прямой Wi-Fi этим тестом не доказан. Это не приёмка двух независимых сетей.

Первая сверка `verify-remote-client-run.ps1` прошла: 0:4, round 4, revision 1164, Pending=false, 12 общих authoritative ticks с одинаковыми entity hashes и стабильные logical streams. Windows: Connections=1 / AuthGeneration=6; Android: максимальное Connections=10 / AuthGeneration=14. Владелец подтвердил сворачивания/перезапуски; exit-info содержит `SwipeUpClean`. После возврата Android получил результат того же матча. Это не объясняет каждое повторное соединение и не заменяет контролируемую проверку стабильности. Отображение драфта/боя подтверждено владельцем; один автоматический screenshot поймал Unity splash при перезапуске и не считается visual acceptance.

Receipt первой успешной проверки: `Logs/RemoteAndroid/cloud-70537c7dc3e6/first-human-acceptance.json`; Windows run `Logs/RemoteClient/4b2f31d6d5bb48e3b45cca259f5d5f74`. Поздний экспорт Android после возврата в меню уже содержит Bot assignment и корректно не проходит human verifier. Он перезаписал первоначальный client-1 snapshot; поздние файлы отдельно сохранены в `post-match-export/`, граница воспроизводимости — `evidence-note.md`. Не представлять поздний набор как отдельный PASS и не восстанавливать отсутствующее assignment из итогового receipt.

Причина повторного поиска — QA autoqueue флаг сохранялся после возвращения в меню. `RemoteMatchLauncher` теперь потребляет его перед первым автоматическим `TryLaunch`; обычные нажатия, retry и серверные таймеры не изменены. Исправление скомпилировано, APK установлен, errors=0 / warnings=3, target/product восстановлены. SHA-256 текущего APK: `f054b32e0e045feceb7c36b5923bba9fc27a6aa2ecc662a71d154822a02df75e`; receipt `Logs/RemoteAndroid/device-preparation-20260914/one-shot-build.json`. Повторный облачный матч именно на этой точечной QA-правке не выполнялся.

Все три deployment получили `Terminated`. Текущие Android/Windows bootstrap удалены, клиенты остановлены; provider keys и заранее созданные identity вне репозитория сохранены. Azure и платные тарифы не активировались. Следующие проверки: доступность Edgegap через LTE (причина TCP refused не установлена), контролируемый непрерывный Android матч, отдельный lifecycle/fault сценарий и immutable per-match evidence до возврата в меню.

## Regional probe c3ac3f3da201 (2026-09-14, Moscow time)

Текущий Edgegap Free Tier probe получил placement hint около Москвы без auto-US IP, но фактически размещён в Stockholm, Sweden: external port `30160`, 0.5 vCPU, 1 GiB, максимум 15 минут. На Android с Wi-Fi и VPN OFF активен `wlan0`; TCP до endpoint отвечает plaintext HTTP `400`. На ПК default TLS `/readyz` вернул `200`: первый запрос 1006 ms, затем 86/84/84/84 ms.

Windows run `0137bdebcb8346158133379fb3bbe884` и Android получили общий human match при Android Wi-Fi/VPN OFF. Stability acceptance провален: владелец наблюдал постоянные восстановления и лаги; Android записал 30 connections и 7 Battle frames, PC — 1 connection и 219 Battle frames. PC median updates 183 ms против прежних Dallas 283 ms (это не RTT); p95 около 217 ms. Shared BattleTicks отсутствуют, consistency entity hashes не проверена. Тест намеренно остановлен в round 5 при 2:2, поэтому полной приёмки нет.

Снятый capture: `Logs/RemoteAndroid/cloud-c3ac3f3da201/final-capture` (ignored). Bootstrap удалены, клиенты остановлены. Terminate запрошен через UI в 23:22 UTC; UI подтвердил статус `Terminated`. Evidence reachability: `Logs/RemoteAndroid/cloud-c3ac3f3da201/regional-probe.json` (ignored).

Новая Android diagnostic APK собрана `Succeeded`: errors=0, warnings=3, исходный target восстановлен на Windows64; ADB install `Success`. SHA-256: `de0009d5ed7090d1844117e396bcd351e76d0eab65bd159fce17bda98a9051ae`. Renewal fixture исправлен только в test file через реальный `LocalProcessCredentials` с тем же TLS guard. Focused tests: 4/4 PASS; полный набор ServerClient: 66/66 PASS.

Для следующего прогона transport сохраняет максимум 32 type-only diagnostic JSONL records без message, stack, URI или credentials. Android diagnostic APK установлена и одиночный клиент запущен на `242149d2c307` (Stockholm, external port `30921`, Free Tier 0.5 CPU / 1 GiB / максимум 15 минут). Direct route дошёл до `OperationStatus` / `Stage=queue-status`, затем получил `TaskCanceledException` при `HasInstance=true` и `HasLobby=true`; до WSS диагностика не дошла, текущий match не подтверждён. Старый assignment файл `c3ac3f3da201` не является evidence этого запуска. Сравнение VPN ON по ответу владельца не получено и не выполнено. Остановка диагностического сервера запрошена около 23:34 UTC до 5 минут; UI затем подтвердил `Terminated`; клиент остановлен, bootstrap удалён. Evidence: `Logs/RemoteAndroid/cloud-242149d2c307/receipt.json` и `Logs/RemoteAndroid/cloud-242149d2c307/direct/queue-diagnostic.json` (ignored). Следующий шаг — A/B same host VPN OFF/ON с новой diagnostic APK.

## A/B/A route diagnostic 66bc55a0081e (2026-09-14, morning)

На Stockholm external port `31895` использованы та же APK `de000…1ae`, тот же телефон и Wi-Fi. VPN OFF дошёл до `queue-status` и получил `TaskCanceledException`. При VPN ON владелец подтвердил работу VPN и активный `VPN agent=1`: один connection за 132.388 s, 131 total frames и 116 Battle frames. Same-round intervals: count=114, median 155.993 ms, p95 198.768 ms, max 1176.399 ms. Владелец отметил с VPN улучшение, но короткие stutters остались. После повторного VPN OFF (`VPN agent=0`) записаны 13 `Poll`/`Receive` timeout 5025–5069 ms при `Suspended=false`, затем `Acquire` timeout на 10035 ms; максимум connections=15 и пользователь наблюдал постоянное восстановление.

Это server Bot diagnostic, не human acceptance. Наблюдение подтверждает зависимость от маршрута, но не устанавливает ISP block или Unity bug: точная причина неизвестна. Клиент остановлен, bootstrap удалён; UI подтвердил `Terminated`. Evidence: `Logs/RemoteAndroid/cloud-66bc55a0081e/receipt.json` и `Logs/RemoteAndroid/cloud-66bc55a0081e/final-capture` (ignored).

## Frankfurt probe 744e28025e59 (2026-09-14)

Frankfurt Free Tier (0.5 CPU / 1 GiB) проверялся с Android Wi-Fi ON / VPN OFF; PC `/readyz` cold вернул `200` за 557 ms. Телефон более двух минут показывал UI «Подключаемся к серверу». Queue diagnostic, current assignment и transport diagnostic не появились, поэтому точный pending stage неизвестен. Не доказана блокировка конкретного ISP или порта. Screenshot: `Logs/RemoteAndroid/cloud-744e28025e59/current.png`; receipt: `Logs/RemoteAndroid/cloud-744e28025e59/receipt.json` (ignored). Телефон остановлен, bootstrap удалён; UI подтвердил `Terminated`. Stop request: `2026-09-14T08:06:40.603Z`, processed: `2026-09-14T08:06:40.638Z`.

Production-проблема не исправлена. Следующий шаг: независимый deadline/диагностика зависшего подключения и решение доступности маршрута, затем human/fault matrix.
