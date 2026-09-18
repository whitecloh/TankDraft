# Запуск ПК QA-клиента владельцем
Status: owner PC launcher verified; external tester distribution remains separate
Last reviewed: 2026-09-17

## Причина закрытия

В Player.log владельца от 17.09 15:10 есть `FUSION_START_FAILED` после нормальной загрузки Unity. `FusionDedicatedBootstrap` требует `TANKDRAFT_FUSION_RUNTIME_PATH`; при запуске exe двойным кликом переменная отсутствует, bootstrap вызывает Quit(1). Это штатное аварийное завершение QA bootstrap, а не доказательство графического краша.

## Вход

После запуска сервера и состояния Ready открыть `Builds/Fusion/Client/StartGame.cmd`. Канонический launcher — `Tools/Fusion/open-game-client.cmd` + `open-game-client.ps1`; build authoring копирует cmd к клиенту.

Launcher использует существующий `prepare-editor-play.ps1`: обновляет только ранее созданный tester0, проверяет текущий server instance, передаёт приватный runtime path дочернему процессу. Автоматического поиска матча/выбора усилений нет. Server Manager и игровой сервер сами не запускаются. Серверные ключи не копируются в билд.

Editor и этот клиент используют один QA-аккаунт: перед запуском остановить Play и закрыть предыдущий клиент. Launcher проверяет уже запущенный Player и свежий heartbeat Editor. Это защита обычного последовательного запуска, не замена серверной авторизации и не универсальный account lock.

Окружение процесса ограничено: QA env очищаются только у дочерней игры, системная ExecutionPolicy/переменные не изменяются. Windows PowerShell 5.1 получает собственный PSModulePath. Ошибки подготовки показываются без содержимого credentials; журнал игры находится в отдельном Logs/FusionEditor/<run>/player.log.

Этот вход предназначен для компьютера владельца с проектом, Server Manager и настроенными приватными QA identity. Перенос папки Client другу не создаёт ему авторизацию: внешний тестовый пакет требует отдельного следующего шага. Не переносить папку .codex-secrets.

## Проверка 17.09

- Stopped server: launcher завершился кодом 1 с объяснением запуска панели, Player не стартовал.
- Сборка клиента 15:18:53: Succeeded, errors=0, bytes=119030143; `StartGame.cmd` автоматически скопирован. Включает последние queue UI/config исправления. MCP-вызов сборки потерял ответ; результат подтверждён build report, обновлёнными артефактами и реальным запуском.
- Фактический запуск `cmd.exe /d /c Builds/Fusion/Client/StartGame.cmd`: exit=0, клиент PID9000 остаётся Responding=True, FUSION_CLIENT_CONNECTED, очередь Status Idle. Журнал: `Logs/FusionEditor/78eadf3f2f6d468fb4e82605971df833/player.log` и `queue.jsonl`; instance8323366b5d5d4134adeb3a22ce9162fd.
- Повторный launcher при работающем Player: exit=1 с сообщением already running, второго процесса нет.
- PowerShell parser и scoped diff check PASS. Приватные настройки/сохранения/экономика не менялись, обновлялся только ранее разрешённый QA auth token. Перед положительной проверкой первый рестарт server auth временно вернул operation_failed; повторный явный старт успешен.
- Подтверждён вход в меню, не сыгран новый матч и не проверено внешнее устройство. Клиент оставлен открытым для владельца, сервер Ready.
