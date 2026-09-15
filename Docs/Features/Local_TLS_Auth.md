# Локальный TLS и обновление доступа
Status: local Windows WSS, access renewal and two-client recovery verified; production/mobile pending
Last reviewed: 2026-09-13

## Граница этапа

`LocalTlsHost` — локальный WSS adapter standalone сервера. Он слушает только `127.0.0.1:18783`; адрес нельзя заменить внешним через аргумент или environment. PlayFab login, Azure и MPS этим host не вызываются. Интерфейс `IMatchCredentials` оставляет отдельную точку подключения настоящей identity; QA grant не является реализацией PlayFab authentication.

`ServerClientSettings.asset` задаёт content/protocol/presentation настройки; production UI и ECS не получают полномочий сервера. Протокол `tankdraft-server-v2` намеренно несовместим с прежним loopback WS v1. Legacy socket verifier сохранён отдельно.

## Доступ и повтор команд

Launcher создаёт две identity и случайные 256-bit grants. Grant, public certificate pin и endpoint передаются своим дочерним Player через environment; токены не попадают в CLI, journal, snapshots или логи. Это локальный QA механизм, а не защита от администратора той же машины.

`POST /v1/local-session` по HTTPS принимает только provisioned grant. Выдача не принимает account, side или match от клиента. Access действует до 30 секунд, grant до 900 секунд, запуск до 850 секунд; ограничения в `Backend/Config/local-tls.json`. Повтор выдачи до окна обновления возвращает тот же access, поэтому потерянный HTTP-ответ не вызывает лишней ротации. Задержка следующего обновления считается от оставшегося TTL. Отзыв или истечение grant запрещает и обновление, и использование access.

Физические `SessionId` и access token меняются; серверный `StreamId` сохраняется. Domain command gate использует этот stream для последовательности команд. При обновлении старый access отзывается; проверка access и выполнение команды синхронизированы с ротацией на одной блокировке family.

Welcome подтверждает match/side/content/protocol, SessionId/StreamId/generation и `NextSequence`. Клиент сверяет привязку с полученным доступом и собственным journal. Подмена stream, другого матча или несовместимая последовательность останавливает транспорт. Смена токена не переписывает `OperationId`, `Sequence` и payload уже сохранённого intent. Серверный durable receipt возвращает прежний ACK без повторной мутации, включая восстановление SQLite runtime. Journal хранит только команды и public stream ID.

Плановое обновление теперь передаёт Reauthenticate по тому же WSS. Сервер проверяет account/match/side/StreamId/generation и новый bearer, клиент проверяет точный ответ. Cursor, pending intent и presentation buffer сохраняются. CatchingUp означает ожидающую серверную работу, а не разрыв сети: решения задерживаются, воспроизведение продолжает читать кадры.

Refresh выполняется перед истечением access; `4401` требует повторного получения доступа. При reconnect клиент запрашивает текущее полное состояние. Серверный scheduler продолжает дедлайны, случайные пропущенные выборы и бой независимо от сокетов. Семейства grant пока живут только в процессе; восстановление всего auth host с production identity — отдельная работа.

## TLS и ограничения протокола

Для каждого запуска генерируется RSA-2048 self-signed сертификат с SAN IP `127.0.0.1`, server-auth EKU и сроком два часа. Клиент проверяет точный SHA-256 leaf certificate, имя, даты и ошибки цепочки; разрешается только ожидаемый UntrustedRoot локального сертификата. Проверка применяется к конкретным HTTPS/WSS соединениям, глобальный callback и trust store не меняются.

В реальном Windows Player штатный Unity Mono `ClientWebSocket` отвергал self-signed WSS до scoped callback, хотя HTTPS успешно работал. Поэтому `LocalTlsSocket` явно устанавливает TLS 1.2 через `SslStream`, проверяет bounded HTTP upgrade (включая Sec-WebSocket-Accept), затем передаёт поток стандартному `WebSocket.CreateFromStream`. Framing, masking и закрытие остаются в библиотеке. Собственная криптография и отключение проверки сертификата не используются. SHA-1 для Sec-WebSocket-Accept — часть WebSocket handshake, не алгоритм подписи сертификата. API [WebSocket.CreateFromStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.createfromstream) и [SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream).

Windows SChannel требует доступный ему key container: временный PFX импортируется из массива через UserKeySet без PersistKeySet. ОС может использовать временное хранилище ключа; обещания «ключ никогда не касается диска» нет. Код не экспортирует PFX в файл и не устанавливает сертификат в доверенные. Для облака понадобятся обычные доверенные сертификаты и отдельная production trust policy.

До upgrade ограничены headers, соединения и число обращений; одновременно один socket на stream. Более новая generation может заменить старый socket, старая не может вытеснить новую. Origin/query запрещены. JSON имеет ограниченную глубину и размер, неизвестные и повторные поля отвергаются. Приём фрагментированного сообщения и отправка ответа ограничены общим таймаутом. Match pump не ожидает чтения клиентом ответа.

## Проверка и запуск

```powershell
./Tools/Backend/test-client-protocol.ps1
./Tools/Backend/test-tls-session.ps1
./Tools/Backend/test-local-backend.ps1
./Tools/Backend/run-server-client.ps1 -Automated
./Tools/Backend/verify-server-client-run.ps1 -RunDirectory 'Logs/BackendClient/tls-<run id>' -Tls
```

Windows Player собирается через существующий Unity MCP build bridge. Автотест один раз завершает процесс side 0 перед чтением ACK, отзывает его access и запускает Player с прежним journal/grant. Side 1 отключается на 40 секунд, превышая access TTL. QA ошибка записывается отдельным файлом и завершает Player с кодом 2. Это дополнительная диагностика тестового режима.

## Результат проверки 2026-09-13

Последующий тест через зашифрованный TCP fault relay: задержки 40–120 ms, reset/stall и полный матч двух Player — PASS, [Network_Fault_Matrix.md](Network_Fault_Matrix.md).

- Финальный Windows build через MCP: 0 errors / 0 warnings; Editor восстановлен в чистый MainMenu, Edit Mode, без Console errors.
- `test-client-protocol.ps1`: 36/36. Компилируются реальные исходники из Assets через Compile Link. Включены два настоящих SslStream/WebSocket подключения: корректный pin открывает socket, неправильный вызывает AuthenticationException; scoped callback действительно вызывается. Неверный сертификат не разрешается для прохождения теста.
- `test-tls-session.ps1`: PASS базовый и расширенный Kestrel/WSS набор. Проверены JSON выдачи доступа, неверный pin/default trust refusal, active-stream 409 до upgrade, реально committed команда с потерянным ACK, retry без повторной мутации, generation/stream, next sequence после SQLite restart, pending следующего раунда после смены access, malformed messages с проверкой точного close status, expiry 4401 и revoked access 401.
- Регрессия: Security 28/28, Match 22/22, Persistence 18/18, HTTP 46 checks, domain 13 checks, два одинаковых полных headless матча — PASS.
- Реальный run `Logs/BackendClient/tls-e0cd5e52601141e5847fd467fb87beb8`: семь раундов, 3:4, revision 2692. `verify-server-client-run.ps1 -Tls` — PASS; 196 общих боевых тиков, ноль несовпадений полного BattleEntityState. Первый Player перезапущен с pending intent и новым access; второй отключался на 40 секунд. Финальные generation: 7 и 6, StreamId каждой стороны неизменен, оба journal без pending. Итоговые экраны 3:4 проигрыш / 4:3 победа просмотрены. Коды финальных Player записаны: `[0,0]`; launcher завершился 0. Listener остановлен.

Два диагностических запуска до исправления Unity Mono WSS сохранены в ignored Logs (`tls-1df62da8f04e453788667ba741409dfb`, `tls-a28a9567f0654299b91bb28fcf203451`); они не считаются успешной сетевой проверкой. Округление остатка RefreshAfter вверх устранило отдельное ложное «Invalid access response» при повторной выдаче за долю секунды до окна refresh.

Не проверены production PlayFab login, mobile background/kill, сети с loss/jitter, Linux/MPS, 100 CCU и distributed recovery. Этот патч не завершает весь S4 и не открывает zero-charge cloud gate.

## Регрессия CatchingUp (2026-09-13)

Connected больше не зависит от CatchingUp. Ранее ожидающий server tick снимал Connected, из-за чего Unity очищала presentation buffer и показывала Recovering без разрыва WSS. Submit/CanChoose/отправка pending по-прежнему запрещены при CatchingUp. Runtime proof: два последовательных Windows-матча, по одному соединению на матч и пять поколений access; [Matchmaking_and_Bots.md](Matchmaking_and_Bots.md). Transport test специально получает только CatchingUp snapshots через настоящий test TLS/WebSocket и проверяет сохранение Connected/cursor/frames при трёх ротациях.
