# Сетевой match prototype
Status: первая Windows/Editor матрица пройдена; полная приёмка сети и релизная инфраструктура открыты
Last reviewed: 2026-09-13

## Границы

Это ранний PUN 2 prototype для двух Windows Player-клиентов. Он не является release backend, не создаёт live-конфигурацию, не меняет профиль и не выдаёт награды. Полная мета, экономика, purchases, рейтинг, production matchmaking и боты при низком онлайне остаются за пределами prototype.

Authority закрепляется за actor, записанным в room properties при создании комнаты. Это не автоматический MasterClient failover: любая сторона, включая pinned host, может reconnect/rejoin в том же процессе с тем же actor в пределах TTL, но замена authority/host не реализована. Потеря authority переводится в ошибку; host replacement требует отдельного решения.

## Поток матча

Authority принимает command envelope с identity матча, версией content, раундом, token и sequence. Повтор sequence возвращает тот же ack с признаком duplicate; stale и неверная версия отклоняются. Офферы передаются получателю по его стороне и не используются как общий публичный список.

Authority продвигает fixed-step simulation и публикует snapshot каждому клиенту. `NetFrame` несёт revision, tick, phase, result, shared hash и события. Отображение использует Photon time и presentation buffer; при пропуске snapshot клиент запрашивает восстановление. Для локального игрока с side 1 ориентация арены отражается только в presentation и не меняет simulation, state или события.

Outcome использует точнейшее simulation time попаданий/смертей. При точной одновременной гибели последних армий только authority один раз выбирает seeded random winner; winner и seed trace входят в общий state/result. Клиенты не делают локальный roll. Правило проверено в production resolver, сетевом codec и серии из 40 матчей.

## Content, сцена и папки

- `Assets/TankDraft/Runtime/Match/Networking/Content/NetworkMatchSettingsAsset.cs` хранит ссылку на local `MatchSettingsAsset`, protocol/room settings, TTL, timeouts, presentation delay и пользовательские строки состояния.
- `Assets/TankDraft/Runtime/Match/Networking/{Core,Transport,Bootstrap}` содержат frame/command contracts, PUN adapter, pinned authority и runtime session. Проверки compile/core, Windows и Editor приведены ниже.
- `Assets/TankDraft/Editor/Networking/Match/NetworkMatchAuthoring.cs` создаёт `NetworkMatchSettings.asset`, копирует `Match.unity` в `NetworkMatch.unity`, сохраняет existing world/cameras/EventSystem/UIMatchScreen и заменяет только local scope своим network scope/transport.
- `Assets/TankDraft/Scenes/Gameplay/NetworkMatch.unity` и `Assets/TankDraft/Configs/Match/Network/NetworkMatchSettings.asset` созданы через Unity Editor; ссылки, запуск и сохранённая сцена проверены.

## Запуск и evidence

Через открытый Unity Editor применяются authoring, validation и очередь build:

```powershell
Tools/Photon/invoke-network-match.ps1 -Action Author
Tools/Photon/invoke-network-match.ps1 -Action Validate
Tools/Photon/invoke-network-match.ps1 -Action Build
Get-Content Logs/TankDraftSetup/NetworkQA/build-status.txt
```

`PASS` в `build-status.txt` является только результатом build. До него `QUEUED` и `RUNNING` не подтверждают сборку.

После успешной сборки launcher создаёт два скрытых Windows Player-процесса для QA с общей уникальной room:

```powershell
Tools/Photon/run-network-match.ps1 -Auto -Faults -Wait
```

`-Auto` включает test-only выборы и paired Continue; `-Faults` включает test-only reconnect guest, disconnect pinned authority и пропуск snapshot; `-Wait` читает JSONL до watchdog. `-tdMatchFps` задаёт 30 FPS host и 90 FPS guest. `-tdMatchCapture` сохраняет snapshot battle/result в отдельные каталоги клиентов. Для ручной игры можно самостоятельно открыть две копии `Builds/NetworkMatch/TankDraftNetworkMatch.exe`: они подключаются к prototype room по умолчанию и ждут ввода. После предыдущей сессии её освобождение занимает до 60 секунд. Также доступна сцена `NetworkMatch` в Editor и отдельный guest через `Tools/Photon/run-network-match.ps1 -Role Guest -Room tankdraft-match-default -Auto`; этот guest делает тестовые выборы, а не представляет человека. Без Auto/Faults диагностические команды не отправляются.

В `Logs/TankDraftSetup/NetworkQA/<room>/host.jsonl` и `guest.jsonl` ожидаются `present` с revision/hash, `complete` с winner/wins/round и, при негативных сценариях, `error` или fault evidence. Launcher сверяет обе стороны, но не заменяет анализ логов, визуальную проверку или device QA. Проверка всех общих revisions: `Tools/Photon/verify-network-match.ps1 -RunPath <каталог прогона> -RequireFaults`.

## Результаты 2026-09-13

MCP compile/core validation — PASS; три Battle scenario дали одинаковые 457/509/320, Match domain — 13 групп assertions. 40 матчей / 219 раундов завершены без `ReviewRequired`, максимум 20.43 с; trace содержит 3 seeded exact ties и corrected near-equal hit group. Local Editor Play Mode — PASS 62 assertions, семь раундов 3:4, неизменный профиль и Game View 576×1280/576×1024. Harness теперь проверяет target size/bounds, а не Scene View 971×602.

Windows build — PASS, 0 warnings, 184938029 bytes. Baseline двух Windows clients: complete 0:4 и 410/410 common hashes. Fault evidence: `Logs/TankDraftSetup/NetworkQA/tankdraft-match-1a8c890a15ca4ba3b21c987216c18ff6/summary.json`: 408 common revisions, guest missing 88/89 намеренно с resync/recovery, 1170 events на сторону, 4 rejected stale/version, 2 accepted duplicates, rejoin host/guest, 0:4 за 4 раунда. UTC presentation difference на одном PC: average 13.46 ms, p95 18.69 ms, max 31.68 ms; это не network-latency limit.

Editor и внешний Windows guest завершили 0:4; units/projectiles наблюдались, четыре screenshot-файла валидны. Инспектирован Editor `Logs/TankDraftSetup/NetworkQA/Editor/02-battle-tick60.png`; hidden Windows captures чёрные и не являются visual evidence. Folder audit: 217 assets, 39 prefabs, 149 original GUIDs; tracked audit содержит 53 pre-existing changes. Editor завершён в чистом MainMenu/Edit Mode без compile errors.

## Непроверенное

Не проверены RTT 50/150/300 ms, jitter/loss matrix, max-load, shared video, Android/iOS и реальные разные сети. Не закрыты release authority, host migration/replacement, settlement/награды и защита от атак; широкие acceptance criteria синхронизации остаются открытыми.
