# Полный боевой экран через Fusion QA
Status: F2 gameplay adapter и два полных 2PC Human матча PASS; сжатие улучшило задержку; menu/reconnect/device и дальнейшая плавность впереди
Last reviewed: 2026-09-14

## Реализация

Authored `FusionGameClient` scene/prefab запускает авторизованный runner, получает lobby/queue assignment и открывает существующий `ServerMatch`. `FusionGameClientFlow` передаёт `IMatchCredentialsFactory` в VContainer scope сцены. SO `ServerClientSettings`, подготовленные боевые prefabs, ECS и серверные правила сохраняются.

`FusionMatchCredentials` получает Session через gateway. `IMatchSocketFactory` — небольшой transport seam существующего клиента; `FusionMatchSocket` адаптирует прежние snapshot/command/reauth сообщения к QA request/reply. URI `fusion://dedicated/v1/socket` и строка `gateway-owned` — локальные маркеры совместимости, не сетевой адрес и не credentials. Маркер удаляется до отправки Reauthenticate; реальные access/lobby tokens находятся на gateway. Журнал намерений изолирован по match hash и side. Renewal и poll сериализуются на одном QA канале; логика идемпотентности и presentation остаётся общей.

Ответы сервера имеют версионированную рамку TDQ1 с Deflate либо raw fallback. Распаковка ограничена 1 048 704 байтами, объявленной длиной и проверкой лишнего/недостающего вывода. Это сжатие, не шифрование. Старый и новый QA player wire несовместимы: сервер и клиенты собираются парой. Внешние JSON-ограничения QA протокола сохраняются после распаковки.

## Воспроизведение

1. Unity MCP вызывает `FusionGameClientAuthoring.RequestBuild()` — последовательная сборка gateway и игрового PC клиента внутри открытого Editor. Отчёты: `Logs/FusionMigration/dedicated-build.txt`, `game-client-build.txt`.
2. Server Manager на loopback18878: приватный `AllowPlaintextQa=true`, запуск до Ready. Эта настройка включена с разрешения владельца; UI явно показывает отсутствие шифрования и выключенную экономику.
3. `Tools/Fusion/test-gameplay-pair.ps1 -MaximumSeconds 420`: две ранее разрешённые QA identity, два настоящих PC Player, автоматические выборы карт. Скрипт проверяет Human assignment, разные стороны, общий матч/счёт и успешные выходы. Runtime-файлы приватные; секреты не выводятся. Тест не меняет настройки Photon, тарифа или экономики.
4. `Tools/Fusion/summarize-gameplay-pair.ps1 -RunDirectory <Logs/FusionServer/game-...>` сохраняет timing-summary.json: sampled battle timings, поколения auth, соединения, общие ticks/hash. `PollMs` включает gateway/authority/ожидание кадра, это не чистый сетевой RTT. `PayloadBytes` — JSON после распаковки, не wire bytes.
5. После теста оператор выполняет drain/stop через менеджер. Скрипт пары закрывает только свои клиенты.

## Доказательства 14.09

- ServerClient **78/78**, Manager **4/4**, RemoteHost **64/64**, включая codec bounds и полный локальный QA матч. `Logs/FusionMigration/Checks/fusion-view-client.trx`, `fusion-view-manager.trx`, `fusion-wire-codec.trx`. Foundation:23 backend projects/35refs. Windows server/client builds succeeded, errors0.
- До сжатия: `Logs/FusionServer/game-ba033cce50074ae5a8f379ff32751840`. Оба Human, общий итог4:2 за6раундов, по1connection, auth generation4. Общих sampled battle ticks6, расхождений entity hash0. Poll median714/683ms, p95 1317/1346ms, max1635/1571ms. Это рабочий flow, но недостаточная плавность.
- Реальный Player screenshot `client-0/battle-1-2.png` просмотрен: существующий HUD/драфт/танки отображаются. В новых Player нет прежней ошибки Addressables settings.json. Screenshot не доказывает FPS или сетевую плавность.
- После сжатия: `Logs/FusionServer/game-3d0eb6c1f805491e8666b12f1e67cc91`. Два Human клиента, общий итог4:3 за7раундов; по1connection, auth generation5, disconnected samples0. Общих sampled battle ticks53, расхождений entity hash0. Poll median383/383ms, p95 520/502ms, max787/670ms. Медианный JSON после распаковки12.7/13.0KB против13.6/13.2KB до сжатия. Это два разных матча, не идентичный replay/контролируемый сетевой benchmark; наблюдаемое улучшение не гарантирует результат на другом маршруте.
- Просмотрен `client-0/battle-0-7.png`: 42vs42 сущности, HUD3:3/round7, authored бой отображается. Обе сборки завершились exit0. После приёмки manager drain завершён, состояние **Stopped**, instanceId пуст; фоновые игровые серверы не оставлены. Панель продолжает работать.

## Следующий этап и пределы

Это отдельный QA entry с автоматическим поиском, не завершённый перенос обычного MainMenu. Затем: кнопка «В бой», отмена очереди, результат/возврат/повторный матч, штатный бот после ожидания; SDK disconnect/rejoin и оба offline; drain при активном матче, restart/loss policy, нагрузка и Android lifecycle. Для плавности остаётся снизить cadence/ожидание ответов: текущие median383ms и хвост787ms ещё не являются окончательной performance acceptance.

Обновление access generation без смены connection подтверждено. Восстановление после разрыва самого Photon runner ещё не реализовано: текущий bootstrap завершает приложение при disconnect. Сохранность результатов при падении хоста, независимые сети и Android не приняты этим 2PC тестом. Экономика выключена, шифрование откладывается по решению владельца.
