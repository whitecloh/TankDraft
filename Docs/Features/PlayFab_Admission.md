# PlayFab identity и допуск к матчу
Status: identity/admission and local RemoteHost verified; live PlayFab identity/admission PASS 2026-09-13; cloud queue/storage/PvP not accepted
Last reviewed: 2026-09-13

Этап R1.3 из [Functional_Release_Plan.md](Functional_Release_Plan.md). Первый Edgegap readiness контейнер уже проверен и остановлен; новый игровой endpoint этим патчем не публикуется.

## Контракт

`UnityPlayFabSessionSource` использует официальный Unity SDK, отдельный `PlayFabClientInstanceAPI` и существующий provisioned CustomId. `CreateAccount=false`; Title secret в клиенте отсутствует. Вызов SDK отправляется на главный поток Unity, device/focus telemetry выключены в instance settings. Ticket хранится только в памяти, локальный cache — 60 секунд; параллельные запросы разделяют один SDK login. Отмена клиента не запускает новый SDK login, пока старый callback не завершился. Это предотвращает накопление запросов, но застрявший SDK callback требует восстановления владельца источника; это ещё не device-runtime acceptance.

CustomId для такого анонимного теста сам является credential. Он должен поступать из приватного хранилища платформы; его нельзя записывать в SO, APK, логи или использовать имя устройства вместо случайного значения. Реальное mobile secure storage, привязка аккаунта к платформе и публичная регистрация пока не реализованы. Новые titles PlayFab по умолчанию запрещают client-side anonymous creation; для первых тестов используется контролируемое server-side создание двух аккаунтов. Защита title не отключается. [Официальная схема anonymous login](https://learn.microsoft.com/en-us/xbox/playfab/identity/player-identity/platform-specific-authentication/anonymous-login).

`PlayFabMatchCredentials` получает доверенное назначение (WSS endpoint, match, side, content version) и источник ticket через зависимости. Endpoint обязан иметь DNS host и `/v1/socket`, без userinfo/query/fragment; session URL — HTTPS `/v1/session` того же origin. Проверка TLS стандартная; локальный pin не применяется. Credentials не переключаются автоматически по environment на произвольный public URL. `ServerClientSession` теперь получает фабрику; `ServerClientScope` сохраняет фабрику родительского scope, иначе использует прежнюю локальную. Queue handoff по-прежнему локальный и требует отдельной реализации.

Клиент отправляет `POST /v1/session`, `Authorization: Bearer <PlayFab session ticket>`, тело только `{ContentVersion, OperationId}`. Account, match и side из тела не принимаются. Ticket — opaque printable ASCII до 4096 символов; operation сохраняется при потере ответа. Ответ содержит `AccessToken`, `SessionId`, `StreamId`, `MatchId`, `Side` (целое), `Generation`, `ExpiresInSeconds`, `RefreshAfterSeconds`. Клиент строго проверяет поля/тип/назначение, размер до 16 KiB и общий deadline 10 секунд, включая получение ticket и ожидание предыдущего запроса. Токены не входят в журнал команд.

Серверный `PlayFabIdentityAdapter` валидирует ticket по фиксированному `https://{titleId}.playfabapi.com/Server/AuthenticateSessionTicket`. PlayFab SDK DTO сохранены; сетевой transport отдельный, с default TLS, redirects/cookies off, connect timeout 5 секунд и общим deadline 10 секунд. Ответ ограничен 256 KiB, strict UTF-8, depth 32; дубли полей (включая разный регистр), неверные типы/envelope отвергаются. Expired ticket даёт отказ identity; недоступность провайдера даёт временный отказ. Нет автоматических retry. [AuthenticateSessionTicket](https://learn.microsoft.com/en-us/rest/api/playfab/server/authentication/authenticate-session-ticket?view=playfab-rest).

`MatchAdmissionService` принимает только серверный список `MatchAssignment`, находит место по проверенному PlayFab account и проверяет content/deadline. Срок access не превышает 120 секунд, свежесть identity и срок assignment. Access tokens — 32 криптографически случайных байта; lookup по SHA-256, fixed-time comparison. Один новый успешный operation отзывает старый access: правило текущего компонента — последняя успешная авторизация получает доступ. Старые команды должны каждый раз проходить `UseAccess`, а WebSocket host обязан проверять generation, не сохранять однажды проверенного caller навсегда.

Стабильный StreamId живёт до конца assignment, даже если access истёк. Повтор текущего operation после потери ответа возвращает тот же действующий access; после expiry перевыпускает его с тем же stream. Повтор старого operation после нового не отзывает актуальное соединение. История операций и generation ограничены; при исчерпании — явный отказ без удаления истории и без перезапуска командной последовательности. Текущая реализация process-local: state не переживает уничтожение контейнера и не заменяет distributed lease/durable assignment.

`AdmissionHttpEndpoint` добавляет только session route: HTTPS обязателен; plain HTTP разрешён лишь явно включённой loopback verification. Не доверяет сырым forwarded headers. Origin/cookies/query и лишние JSON поля запрещены; body до 2 KiB, bounded inflight/rate, `no-store`, санитарные пустые ошибки. `TankDraft.RemoteHost` локально соединяет admission с authenticated queue и WSS, проверяя auth на каждом HTTP/WS message; cloud ingress, managed Edgegap TLS и Unity wiring не приняты. В готовый readiness image этот endpoint не включён.

## Локальные проверки

```powershell
./Tools/Backend/verify-playfab-identity.ps1
./Tools/Backend/verify-admission.ps1
./Tools/Backend/verify-admission-http.ps1
./Logs/BackendSdk/dotnet.exe test Backend/TankDraft.ServerClient.Tests/TankDraft.ServerClient.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~PlayFabMatchCredentialTests
```

Проверено: 36 identity HTTP/SDK-DTO cases, 19 admission cases, 46 HTTP/core assertions, 18 client credential tests. HTTP/core suite реально открывает loopback listener: две подставленные PlayFab identity получают серверные места; command ACK повторяется после rotation без повторного выполнения; затем 10 минут игрового времени без действий клиентов, сервер сам завершает 5 раундов 4:1, вернувшийся клиент видит result с прежним stream/receipt. PlayFab ответы в этих проверках fake; это не облачный бой двух устройств.

Unity MCP подтвердил импорт новых классов и успешную компиляцию. Запуск SDK login на устройстве пока не выполнялся. Попытка полного `ServerClient.Tests` имела 4 отказа старых socket integration tests из-за занятого `127.0.0.1:18783` существующим LocalHost; последующий набор с явным исключением этой группы прошёл 53/53. Чужой/ранее запущенный QA host не остановлен ради тестов. Финальное evidence: `Logs/PlayFabAdmission/098b2802e8114e55bee1b12be658fd06` (identity/admission/http-core/client logs, client.trx, locked live-probe build).

## Первый реальный PlayFab тест

1. Владелец берёт существующий ключ B16D9 в PlayFab → Tanks → Settings → Secret Keys. Основной путь без PowerShell-скриптов: открыть уже созданный приватный файл `C:\Users\ramze\.codex-secrets\TankDraft\playfab-server-key.txt` в Notepad, вставить **только значение ключа** без кавычек, имени, пробелов или иных строк, нажать `Ctrl+S` и закрыть Notepad. Секреты в чат не отправлять. `Tools/Backend/set-playfab-server-secret.ps1` остаётся необязательным вариантом, но в оболочке владельца его запуск заблокирован политикой подписей PowerShell; ExecutionPolicy ради этого не меняется.
2. `Tools/Backend/verify-playfab-live.ps1 -ProvisionTwoTestPlayers` создаёт не более двух стабильных тестовых identities через Server/LoginWithCustomID; случайные CustomId записываются в приватный файл ДО первого запроса, чтобы потерянный ответ не создавал новые аккаунты. Далее Client/LoginWithCustomID с CreateAccount=false, server verification и admission/reconnect. Первый успешный прогон — 10 ограниченных API calls, без автоматических повторов.
3. Следующие прогоны: `verify-playfab-live.ps1` без provision switch. Нет создания аккаунтов; проверяются те же двое. Сырые ответы, tickets, CustomId и account IDs не записываются в output. Скрипт проверяет ACL/отсутствие link у credential пути. Запуск требует действующего Development/no-charge режима title; если условия изменились, сначала сверить их.

13.09.2026 выполнен один явный запуск `verify-playfab-live.ps1 -ProvisionTwoTestPlayers`: PASS, exit 0. Подтверждены два разных test players, client login, server verification, assigned seats, lost-reply retry и reconnect rotation; выполнено 10 API calls. Tickets и account IDs в output не выводились. Evidence: `Logs/PlayFabAdmission/live-20260913-224701/live-acceptance.json`. Ключ сохранён приватно вне репозитория; повторный запуск для этой записи не выполнялся. Это live identity/admission acceptance, но не remote PvP, durable Azure или cloud composition.

Read-only Azure audit 13.09.2026 завершён: существующая подписка имеет нужные сервисы, но provider-enforced no-charge/hard spending cap не подтверждён. Azure ресурсы, permissions и config не менялись; подписку нельзя считать бесплатной. Следом: secure identity storage и queue/assignment composition → durable cloud journal/fencing/drain → два клиента из разных сетей. R1 остаётся открытым.
