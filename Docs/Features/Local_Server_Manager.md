# Локальное управление сервером
Status: панель пересобрана; закрытый plaintext gameplay opt-in включён; первый полный Fusion 2PC Human матч PASS
Last reviewed: 2026-09-15

15.09 исправлен cold start через open-server-manager.cmd/Windows PowerShell5.1: typed catch HttpRequestException падал с TypeNotFound, поскольку System.Net.Http ещё не загружена. Launcher теперь явно загружает встроенную сборку через Add-Type до проверки порта. Ошибка воспроизведена в свежем powershell.exe; после исправления запуск панели при свободном18878 PASS. Дополнительно выявлено наследование PSModulePath от PowerShell7 при запуске .cmd из него: Get-Acl пытался загрузить несовместимый Security module. .cmd очищает PSModulePath только в своём setlocal scope, чтобы Windows PowerShell восстановил собственные стандартные пути. Установка PowerShell7 и изменение системной execution policy не нужны. Игровой сервер автоматически не запускается — кнопка в панели сохраняется.

Актуальный запуск игры из Editor: [Fusion_Editor_Play.md](Fusion_Editor_Play.md). После Ready в панели обычный Play из MainMenu готовит tester0 и входит через FusionGameClient; run-matchmaking.ps1 больше не нужен для этого пути. Старые F1/readiness ограничения ниже являются историей предыдущих этапов.

Финальная проверка launcher15.09: реальный cold .cmd после закрытия только неактивного manager PASS; повторный .cmd из PowerShell7 PASS; прямой ps1 из PowerShell7 PASS. Панель доступна, игровой сервер Stopped/instanceId=null. Системные переменные и политика выполнения не менялись.

Обновление: разрешён закрытый QA без шифрования. Исходники поддерживают приватный `AllowPlaintextQa` (default false), прокидываемый в gateway и private authority; capability `plaintext-qa-no-economy`. Подробнее Photon_Plaintext_QA.md. Панель/exe пересобраны; приватный AllowPlaintextQa=true включён, UI показывает действующий режим. Backend64/64 + Manager4/4 PASS. Полный 2PC игровой тест: Fusion_Gameplay_QA.md.

## Запуск

Двойной клик `Tools/Fusion/open-server-manager.cmd` открывает `http://127.0.0.1:18878/`. Панель работает отдельно от Unity Editor. .NET SDK берётся из `Logs/BackendSdk`; при первом запуске приложение собирается в `Builds/Fusion/Manager`. Закрытие вкладки не выключает сервер.

- **Запустить сервер**: стартует authoritative .NET runtime и отдельный Unity Fusion процесс. Состояние Ready появляется только после heartbeat Fusion. В plaintext QA режиме поддерживает ограниченный игровой протокол для allowlisted QA accounts; без opt-in остаётся readiness-only.
- **Завершить матчи и остановить**: закрывает очередь, ждёт окончания активных матчей и останавливает процессы. В локальной проверке без матчей останавливается сразу.
- **Остановить принудительно**: прерывает процессы; незавершённые матчи аннулируются без наград и штрафов.
- **Проверить локальное ядро**: запускает только .NET runtime без соединения с Photon и без вызовов PlayFab API. Статус AuthorityOnly не означает возможность играть через Fusion.

Показатели: очередь, активные/завершённые матчи, возраст последнего такта scheduler, RAM, суммарная CPU-доля процессов относительно логических CPU машины, число запусков и завершений вне команды оператора. Показатели маршрута/плавности клиента не выводятся как подтверждённые по этим метрикам.

В журнал попадают только контролируемые события менеджера. Последние 200 — в панели; `Logs/FusionServer/manager.jsonl` ротируется после 1 MiB. Unity diagnostics — `gateway.log`; lifecycle logs не содержат сырых исключений, tickets или bodies. При диагностике SDK-лога отдельно проверять его содержимое перед передачей третьим лицам.

## Архитектура и ограничения

Панель и .NET authoritative runtime находятся в одном .NET процессе, игровой вход — в отдельном Unity процессе с `GameMode.Server`. Домен и правила не копируются в Unity. Межпроцессный HTTP привязан строго к loopback и требует случайный per-run gateway key. Этот ключ не выдаётся клиентам. После окончательного cutover HTTP/WSS останется допустимым внутренним IPC; внешний клиентский HTTP/WSS стек будет удалён отдельно.

Нет фонового автоматического запуска при включении Windows и нет автоперезапуска упавшей симуляции. Два процесса не дают durable recovery при сбое ПК. Менеджер проверяет сервер каждую секунду независимо от открытой вкладки. Gateway проверяет ядро и завершается при его недоступности. Начальные пределы: 2 active matches, 4 queued, lifetime 30 min, drain за 5 min, identity calls 500. Это локальные ограничения запуска; тарифы внешних сервисов остаются отдельными.

Панель слушает только `127.0.0.1`. Host/Origin/Fetch-Site проверки и per-process control token закрывают команды от посторонних сайтов; клиент не может передать произвольную команду, путь к exe или environment. Исключений firewall/router не создаётся. Конфигурация хранится вне репозитория: `.codex-secrets/TankDraft/fusion-server-manager.json`; содержит путь к существующему ключу PlayFab и allowlist, а не сам ключ. Fusion private runtime/auth файлы находятся там же, вне Assets/build.

## Fusion F1

SDK 2.1.2. Авторизованный сервер и два клиента, `IsVisible=false`, relay без NAT punchthrough, client session creation disabled. Сервер допускает только указанные PlayFab UserId после Photon Custom Auth. Текущий протокол разрешает только ограниченное `Ready` → `AuthorityReady`, максимум один запрос в работе на игрока. Это не игровой transport и не доказательство полного Human матча.

1. Photon Custom Auth уже настроена на `https://B16D9.playfabapi.com/photon/authenticate`, anonymous=false, reject-if-unavailable=true. Dashboard подтвердил **Free 100 CCU**, subscription 0, burst disabled, 300 GB included.
2. PlayFab Tanks → Add-ons → Photon: Fusion App ID в поле **Realtime App ID**. Форма владельца потребовала также Chat App ID: создан TanksChat `23d5a647-4d6e-4edd-85ad-7e666c04e787`. Получение PlayFab Photon tokens для обоих App ID подтверждено. Прежнее указание оставить Chat пустым не соответствует этой форме; runtime Chat SDK не нужен для боя и не подключён.
3. `Tools/Fusion/prepare-qa-auth.ps1 -CheckOnly` проверяет получение Photon token для существующего тестера. Без switch дополнительно готовит private tokens двух существующих тестеров и одного стабильного отдельного QA server account. Новые credentials не выводятся. После однократного provision сервер обновляет свой токен при каждом старте из панели (2 PlayFab API calls без retries); токены диагностических клиентов обновляются через этот скрипт перед тестом.
4. Unity menu `TankDraft/Networking/Fusion/Build Windows Dedicated Diagnostic`. Через MCP использовать `FusionServerAuthoring.RequestBuild()`, затем проверить файл отчёта и состояние Editor, чтобы tool retry не запускал повторные сборки.
5. Запустить сервер из панели. `Tools/Fusion/test-client-pair.ps1` запускает два headless клиента на 60 s; требуется минимум 20 readiness replies для каждого. Это тест авторизации/связности, а не боя.

## Проверено

- ServerManager tests: 4/4, включая запрет чужого Origin/Host/отсутствующего control token, закрытый gateway ingress, duplicate start, остановку и новый instance после повторного старта.
- RemoteHost regression: 40/40, включая привязку verified Photon identity к PlayFab и локальный gameplay adapter. Evidence `Logs/FusionMigration/Checks/server-manager.trx`, `fusion-account-binding.trx`.
- Реальная панель: старт/heartbeat/память и остановка локального ядра через UI; identity calls 0.
- Unity authored prefab/scene через MCP; Windows build Succeeded, 0 errors, ~118 MB. Это headless Windows Player с отдельным GameMode.Server, не Linux Dedicated build.

- Реальный Fusion server + два PC Client: 60 секунд, по 59 AuthorityReady replies, оба exit code 0. Сервер остановлен после проверки. Игровые данные не передавались.

Фактическое шифрование Fusion пока не включилось: требуется native DatagramEncryption plugin от Photon Support; запрос отправлен, см. Photon_Encryption_Request.md. Ready означает доступность диагностического процесса, не защищённый игровой вход. Локальный адаптер подготовлен, но к Fusion callbacks не подключён.

Полный Fusion QA матч принят отдельно в Fusion_Gameplay_QA.md. Ещё не принято: drain при активном Fusion матче, клиентские reconnect/fault/load, полный menu cutover. См. Fusion_Dedicated_Migration.md.

Продолжение F2: клиентский protocol/channel подготовлен и проверен вместе с authority в LocalOnly полном матче; актуальная RemoteHost suite 49/49 (`fusion-client-protocol.trx`). Панель по-прежнему запускает только readiness gateway. Диагностический prefab обновлён для явного пустого каталога network Addressable scenes; текущий exe остаётся прежним до следующей Windows сборки. Не трактовать этот патч как обновлённый Player или игровой cutover.
