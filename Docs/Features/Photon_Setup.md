# Локальная настройка Photon и проверка транспорта
Status: local configuration implemented; real two-client Cloud transport and rejoin verified on Windows
Last reviewed: 2026-09-12

PUN 2.50 остаётся выбранным SDK. Владелец создал приложение Photon и заполнил настройки. AppId сохранён локально; в документацию, код и результаты QA его не включаем. Это подготовка транспорта, не реализация серверного боевого authority или релизного PvP.

## Конфигурация

- `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` и его `.meta` исключены из Git; существующий GUID сохранён. PUN загружает asset по имени из Resources. Клиентский AppId входит в тестовую сборку, как требуется SDK; это не административный секрет.
- `UserSettings/TankDraft/Photon.local.json` — локальный backup AppId, AppVersion и FixedRegion. Папка UserSettings уже ignored. Схема 1; не добавлять этот файл в Git.
- `TankDraft → Networking → Save Inspector Connection Locally` сохраняет введённые в PUN Inspector параметры и применяет настройки Photon Cloud. `Restore Local Connection` восстанавливает asset из локального JSON, включая случай отсутствующего asset на новом checkout. На другой машине сначала вводится её локальная конфигурация; credentials не переносить через репозиторий.
- Используется версия `tankdraft-dev-001`, FixedRegion `eu`, DevRegion пуст, UDP с fallback, Name Server включён, Server пуст, Port 0, offline mode выключен, Run In Background включён.
- Support Logger отключён: probe записывает свой журнал соединения/событий без AppId и auth token. В журнале есть регион, фактическая версия PUN-приложения и протокол.
- Сохранение/восстановление выполняется только в Edit Mode. Доступны меню PUN `Window → Photon Unity Networking → Highlight Server Settings` и `PUN Wizard`.

SDK AppSettings копируется перед подключением; probe protocol version существует отдельно и не заменяет настроенную AppVersion. MainMenu автоматически к сети не подключается.

## Файлы и границы

| Путь | Назначение |
|---|---|
| Assets/TankDraft/Runtime/Diagnostics/Photon | TankDraft.Networking: PhotonProbeSettings и PhotonTransportProbe; диагностический клиент PUN, без боевой логики |
| Assets/TankDraft/Editor/Networking | TankDraft.Networking.Editor: локальная конфигурация, authoring, binding validation и Windows build |
| Assets/TankDraft/Configs/Diagnostics/Photon/PhotonProbeSettings.asset | Таймаут диагностического runner, число сообщений, TTL и задержка rejoin; это не игровой таймер боя |
| Assets/TankDraft/Prefabs/Diagnostics/Photon/PhotonTransportProbe.prefab | Сохранённый интерфейс и компонент probe; ссылки на config/TMP/buttons настроены заранее |
| Assets/TankDraft/Scenes/Diagnostics/QA/PhotonTransportProbe.unity | Отдельная диагностическая сцена |
| Tools/Photon | Команды настройки/сборки через MCP и запуск двух процессов |
| Builds/PhotonProbe | Игнорируемая Windows Development Mono-сборка диагностики |
| Logs/TankDraftSetup/PhotonQA | Статусы, результаты, player logs и журналы сообщений; ignored |

## Повторение проверки

Запускать из корня TankDraft с открытым Unity Editor этого проекта и MCP на 21509:

```powershell
Tools/Photon/invoke-photon-check.ps1 -Action Validate
Tools/Photon/invoke-photon-check.ps1 -Action Build
Get-Content Logs/TankDraftSetup/PhotonQA/build-status.txt
```

Build ставится в очередь Editor и переживает перезагрузку assemblies до старта. Дождаться `PASS` в build-status; `QUEUED`/`RUNNING` не являются результатом сборки. Сборка выполняется через открытый Editor; параллельный Editor batchmode не запускается. Build list проекта не изменяется, временный выбор Mono восстанавливается после сборки.

После успешной сборки:

```powershell
Tools/Photon/run-two-client-probe.ps1
```

Запускаются два отдельных Windows Player-процесса в headless-режиме, соединяющиеся через реальный Photon Cloud. Общая комната имеет уникальное имя запуска и максимум двух участников; UserId разные. Это не две заглушки и не один клиент с локальной доставкой. Процессы автоматически завершаются; runner при ошибке останавливает только созданные им процессы.

Сценарий: guest отправляет 10 нумерованных запросов, host подтверждает каждый с тем же nonce; guest отключается, host видит inactive actor; guest делает ReconnectAndRejoin в ту же комнату с тем же actor ID; ещё 10 запросов/подтверждений; завершающее подтверждение обеих сторон. Проверяются версия протокола, room, sender, порядок/уникальность sequence и nonce. TTL участника 20 секунд, задержка перед rejoin 1 секунда; общий предел 90 секунд относится только к диагностике. RTT в отчёте — время полного request/echo через второй клиент, не чистый ping до сервера.

PASS требует успешных JSON-результатов обоих процессов и взаимно совпадающих actor/peerActor, room и подтверждённого reconnect. Результат в `summary.json`, путь последнего успешного запуска — `Logs/TankDraftSetup/PhotonQA/latest-run.txt`.

Для ручной проверки можно открыть диагностическую сцену в Editor и ту же Windows-сборку без CLI-аргументов, нажать START HOST в одном и START GUEST в другом. Используется комната `tankdraft-probe-manual`; после закрытия предыдущего сеанса нужно дождаться истечения TTL либо использовать уникальное имя через аргументы. Основную сцену предварительно сохранить. CLI-проверка не проверяет клики/визуальный вывод этой сцены.

## Ограничения

Проверка setup не подтверждает синхронизацию юнитов/снарядов, защиту результатов, серверную симуляцию, packet loss/jitter, смену MasterClient или работу на Android/iOS. Два Player-процесса на одном ПК подтверждают облачный транспорт и rejoin, но не заменяют разные устройства и сети. Эти проверки остаются в Networking_Decision.md и плане боевого spike.

## Результат 2026-09-12

Реальный Photon Cloud в регионе eu, UDP, AppVersion tankdraft-dev-001: оба Windows Player-процесса получили PASS; 20 последовательных запросов и их подтверждения доставлены. Host actor=1, guest actor=2; после Disconnect → ReconnectAndRejoin guest сохранил actor=2, host зафиксировал inactive и возвращение. Первый успешный запуск: `Logs/TankDraftSetup/PhotonQA/td-cd2b57f88e1b4d0b887ee4253d665378/summary.json`. Средний request/echo RTT около 238 ms в этом конкретном запуске; это не замер чистого Photon ping и не целевая задержка будущего боя.

Дополнительно: PASS 7 проверок валидатора config; сохранение → восстановление локального подключения и соответствие Inspector проверены; prefab/config/button bindings корректны. AppId не обнаружен среди неигнорируемых файлов проекта. По завершении authoring восстановлена чистая сцена MainMenu; пользовательский профиль не использовался.

Итоговая Windows Development Mono-сборка: PASS, 0 warnings, около 184 MB по BuildReport. Повторный запуск итоговой сборки с явной проверкой завершающих подтверждений: `Logs/TankDraftSetup/PhotonQA/td-884147f1221b44779605789fa5a0f52c/summary.json`, оба клиента PASS. В этом запуске средний request/echo RTT около 257 ms, максимум 520 ms; показатели зависят от соединения и диспетчеризации двух клиентов, не являются обещанием задержки боя. Регрессия локального меню: PASS 264 prefab/binding assertions и PASS 38 profile assertions; PlayMode/device suite повторно не запускался.
