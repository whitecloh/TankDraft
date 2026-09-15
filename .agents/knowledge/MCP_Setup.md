# Unity MCP
Status: connected and verified through official CLI
Last reviewed: 2026-09-12

ai-game-developer подключён к открытому TankDraft. Codex endpoint: http://localhost:21509/p/26fc6749 . Server base для CLI: http://localhost:21509 . Route создан плагином именно для TankDraft.
Bootstrap разрешён штатным UPM/CLI. После подключения scene/prefab/assets операции выполняются через MCP.
Установлены Core 0.89.0 как в Chibi и CLI 0.90.0. Порт 21509 проверен и занят сервером TankDraft; Chibi 21507 и другой проект 21508 не изменены. Editor запускает сервер из Library/mcp-server/win-x64. Не обновлять версии автоматически.
Машинные параметры хранятся в ignored UserSettings; секреты не помещать в docs или .codex.
Проверено: endpoint отвечает; Application.dataPath = U:/UNITY_PROJECTS/TankDraft/Assets; Edit Mode, не compiling/updating; Console доступна. При установке/перезагрузке были временные transport ошибки, не ошибки игрового кода. Состояние финальной проверки записано в Setup_Verification.md.
Конфигурация локальная: Custom, streamableHttp, authOption=none, host localhost:21509, keepConnected/keepServerRunning=true. Плагин может создать локальный token; не выводить полный UserSettings config. Этот локальный endpoint не предназначен для публикации в сеть.

Bootstrap на чистой машине: npx --yes unity-mcp-cli@0.90.0 install-plugin <project> --plugin-version 0.89.0 --with-server. Затем выбрать Custom local host в ignored UserSettings/AI-Game-Developer-Config.json, открыть/обновить Editor и дождаться импорта. bootstrap-local требует token и не используется для authOption=none. Модальные import/update уведомления могут блокировать main thread и давать timeout MCP.

Пример проверки: npx --yes unity-mcp-cli@0.90.0 run-tool editor-application-get-state U:\UNITY_PROJECTS\TankDraft --url http://localhost:21509 --raw . Для структурированных параметров применять --input-file с JSON. script-execute: полный класс/static method и явные className/methodName.
Команда, вызвавшая reload, может быть повторена клиентом после потери ответа. Скрипты изменений должны быть идемпотентными либо отказываться перезаписывать готовый результат; после timeout сначала прочитать фактическое состояние.
Native Codex MCP может потребовать новую сессию из trusted проекта после появления .codex/config.toml. Не выдавать работающий CLI за автоматически загруженные native tools.
Документация: https://github.com/IvanMurzak/Unity-MCP ; https://learn.chatgpt.com/docs/extend/mcp?surface=cli .
