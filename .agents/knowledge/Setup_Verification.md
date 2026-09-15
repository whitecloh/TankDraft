# Подготовка TankDraft — проверка
Status: foundation and geometry checks passed; gameplay and visual parity not implemented
Last reviewed: 2026-09-12

Рабочая ветка codex/tankdraft-foundation. Chibi Arena не изменялся. Прежние изменения DOTween, удаления HubForceResolve и activeInputHandler=Both сохранены. Коммит/публикация не выполнялись.

## Проверено
- MCP через официальный unity-mcp-cli 0.90.0: Editor state, scene-list-opened, Console, script-execute; подтверждён Application.dataPath TankDraft. Native MCP tools в текущей сессии Codex не опубликованы; рабочий CLI является клиентом того же MCP сервера.
- UPM resolve: UniTask/ECS commit pins, VContainer 1.18.0, MessagePipe 1.8.1, Localization 1.5.9, UI Motion 0.10.6, Core MCP 0.89.0. PUN 2.50 core и editor assemblies загружены.
- EditorUtility.scriptCompilationFailed=False, EditorApplication.isCompiling=False. Portrait установлен. При финальной проверке Console за последние две минуты новых Error не было.
- node Tools/Previz/verify.cjs: 1056 сочетаний пресетов валидны; malformed geometry/color/IDs/viewport отклоняются. Это structural checks, не 1056 визуальных проверок.
- В браузере визуально проверены арена и драфт, auto-fit полного portrait, три отдельных оффера. Добавление элемента и input-handler текста проверены по отображаемому JSON; исправлена прежняя потеря изменений при вводе. Экспорт создаёт blob-ссылку, но download event/файл в IAB не подтвердился.
- Через MCP C# importer валидировал 11 экспортированных normal layouts, отклонил неизвестную schemaVersion и создал Draft_Layout_Smoke.prefab. Все 9 RectTransform имеют координаты/размеры fixture. Original active scene, scene count и dirty state сохранены.
- Scoped git diff --check собственных tracked правок; JSON/TOML проверены. Глобальный diff --check до этой работы уже находил trailing whitespace в пользовательских DOTween modules; чужие файлы не очищались.

## Ограничения и исправленные проблемы
При setup были временные MCP transport ошибки/domain reload и модальные Unity import уведомления. Roslyn script-execute не разрешил Newtonsoft в одном установочном скрипте; замена на подготовленный manifest решила установку. Это не текущие compilation errors проекта.
SDK API Updater адаптировал два Photon Rigidbody view к Unity 6. Штатный subset ImportPackage не создал файлы; SDK восстановлен через MCP с оригинальными asset/meta, затем Refresh. Повторный вызов установки после reload был остановлен guard-ом от перезаписи; фактический результат проверен.
Первый importer с NewScene(Additive) не работал при Untitled. Заменён на NewPreviewScene; повторная проверка прошла. Изолированный camera PNG capture дал пустой фон и не используется как доказательство визуальной корректности; неудачный PNG удалён.

Не запускались: PlayMode/full Test Runner, device build, Android/iOS runtime, online Photon, headless server, backend/IAP/Ads. Они не реализованы на этом этапе. Остались выбор финальных ассетов, утверждение визуального дизайна, сетевой spike и детали неизвестных правил референса.

Для открытой панели превизов оставлен локальный static server 127.0.0.1:8766 (только Tools/Previz). Сам HTML работает без него при открытии обычным браузером. Временные установочные файлы и проверки находятся в ignored Logs/TankDraftSetup.
