# Локальная проверка жизненного цикла PlayFab GSDK
Status: three local agent lifecycle scenarios and 12 boundary checks passed; no cloud deployment or Unity transport migration
Last reviewed: 2026-09-13

## Объём и запуск

Отдельный `Backend/TankDraft.GsdkLocalProbe` запускается официальным LocalMultiplayerAgent (LMA) и использует настоящий C# GSDK 0.11.210519. После назначения сессии он создаёт существующий `DurableMatch` с тем же authored JSON и LeoECS ядром. Клиентов в этой проверке нет: roster содержит два QA identity, connected players передаются как пустой список. Это проверка hosting lifecycle, не сетевого PvP или PlayFab login.

```powershell
./Tools/Backend/bootstrap-lma.ps1
./Tools/Backend/test-gsdk-lifecycle.ps1
```

Можно ограничить запуск `-Scenario complete`, `interrupted` или `before-allocation`. Нужен локальный .NET SDK из `Logs/BackendSdk`. Никакие ключи и реальный Title ID для LMA не требуются. Dummy TitleId `00000`, BuildId/session UUID и SessionCookie генерируются только для локального агента; они не создают объекты в PlayFab.

Скрипт собирает server package, создаёт отдельный run directory и `MultiplayerSettings.json`, запускает агент скрыто, проверяет реальный listener и завершение процессов. Лимит ожидания одного сценария — 55 секунд; при зависании останавливается запущенное дерево процессов. Занятый порт 56001 вызывает отказ без остановки чужого процесса. Сценарии идут последовательно, артефакты остаются в ignored `Logs/PlayFabLma/runs`.

## Источник агента и LocalOnly

Используется [PlayFab/MpsAgent v0.12.0-beta](https://github.com/PlayFab/MpsAgent/tree/v0.12.0-beta), commit `a7af68aea68ee73df85ea63ce81692ba439ff476`, MIT. Это доступный upstream release с beta-меткой, не наше утверждение о production-стабильности агента. [Официальный процессный запуск LMA](https://learn.microsoft.com/en-us/xbox/playfab/multiplayer/servers/localmultiplayeragent/run-process-based-gameserver).

Bootstrap клонирует закреплённый исходный код в Logs и меняет ровно одну строку: `.UseUrls("http://*:…")` → `127.0.0.1`. Исходный агент открывает wildcard listener, поэтому готовый release binary не запускается. Framework-dependent Windows build использует установленный .NET 10. Upstream код собрался с пятью предупреждениями устаревших API; функциональность агента не переписывалась ради подавления предупреждений.

`LocalGsdkBoundary` до первого вызова GSDK проверяет:

- config существует внутри каталога этого QA запуска и не превышает 64 KiB;
- heartbeat endpoint — строго `127.0.0.1:<port>` с допустимым портом, без DNS/URL path/query;
- нет повторных полей JSON или неправильного типа endpoint.

Проверка важных входов нужна **до регистрации callbacks**: методы регистрации GSDK сами запускают внутреннюю инициализацию. В запуске нет Docker, Setup.ps1 агента, firewall/admin команд, SAS tokens, загрузки сборки в облако и account API. LMA включает собственные Azure/PlayFab зависимости как dev-tool; они не добавляются в Core/Match/Unity и не используются для cloud provisioning.

## Lifecycle и данные

1. После локальной проверки config регистрируются shutdown/health/maintenance callbacks. GSDK начинает heartbeats в Initializing.
2. После локальной подготовки вызывается ReadyForPlayers. До allocation матч не создаётся и draft timers не идут.
3. При allocation проверяются session GUID, ограниченный SessionCookie, его LocalOnly/scenario/content hash и ожидаемый roster. Game port берётся из `ServerListeningPort`; порт назначен, но игровой listener в этом probe не запускается.
4. Серверный scheduler независимо от клиентов ведёт настоящий матч. Для быстрого QA выбран управляемый clock; значения лежат в `Backend/Config/gsdk-local-probe.json`, это не новые настройки игрового баланса.
5. Shutdown callback только сигнализирует главному циклу, не трогает БД/матч с heartbeat thread. Цикл прекращает новые итерации, делает `DurableMatch.Flush()`, закрывает БД и проверяет reopen по точному hash.

`Flush()` сохраняет также логическое время между simulation ticks. Обычный Pump может не записать такой шаг, если revision ещё не изменилась и секундный idle checkpoint не наступил. При flush ошибке actor блокируется, прежний committed state остаётся источником восстановления. Dispose освобождает ресурсы и не заменяет явный Flush; аварийное восстановление по-прежнему начинается с последнего commit.

После shutdown **нельзя снова вызывать ReadyForPlayers**: в проверке установленный GSDK возвращал сервер в StandingBy и позволял повторное назначение. В probe terminal state закрывает этот путь; callback во время initialization/allocation также запрещает создание матча после остановки.

## Подтверждённые сценарии

- `complete`: Initializing → StandingBy → Active → Terminating; полный реальный матч до четырёх побед, один pending result в outbox, reopen совпадает.
- `interrupted`: агент завершает процесс во время активного ECS боя; phase/tick и hash после Flush/reopen совпадают, результат ещё не выдан.
- `before-allocation`: агент завершает Initializing; матч/БД не создаются, сервер не возвращается в StandingBy/Active.
- 12 проверок LocalOnly boundary, включая внешний адрес, wildcard, нестандартные URL/порты, дубликаты и неверные JSON-типы.
- Persistence suite расширена до 18 тестов: добавлены flush между ticks и отказ сохранения при flush. Проверены все 18, включая предыдущие process crash/replay/receipt/outbox сценарии.

В каждом LMA сценарии проверяются и отчёт дочернего сервера, и наблюдаемые агентом состояния, и принадлежность listener `127.0.0.1:56001` запущенному процессу. Локальные health callbacks вызываются. Maintenance callback зарегистрирован, но событие обслуживания в этой серии не подавалось и не считается проверенным.

Финальный прогон: `Logs/PlayFabLma/runs/eb76826cf6154c1a938b1dd391c04a6d`. Complete: счёт 4:1, 17 health callbacks и 1 pending result; interrupted: Battle/tick 59, 10 health callbacks, 0 results; before-allocation: 7 health callbacks, матч не создан. Все процессы штатно завершились.

## Что ещё впереди

Владелец передал Title ID B16D9 и скриншоты Development/$0.00 estimated; ID записан в локальные Unity settings. Условия MPS и жёсткая защита от начислений ещё не подтверждены; подробнее в [PlayFab_SDK_Setup.md](PlayFab_SDK_Setup.md). Production GSDK host/admission/auth, maintenance policy, Linux/MPS, Azure durable storage/lease, потеря VM, реальные клиенты и сеть остаются следующими задачами. Текущий probe использует локальную PauseProcessDowntime и SQLite на сохранном диске; не является автоматическим облачным failover.

Далее S4: transport/auth boundary и Unity snapshots/events/reconnect. [Zero-charge gate](Backend_Implementation_Plan.md) остаётся закрытым; успешный локальный lifecycle не подтверждает бесплатность облачного hosting.
