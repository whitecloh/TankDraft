# Сетевой стек и бюджет тестирования TankDraft
Status: research complete; PlayFab MPS/Azure direction accepted; transport and SDK lock pending spike
Last reviewed: 2026-09-13

## Рекомендация

Владелец 2026-09-13 выбрал попробовать **PlayFab Multiplayer Servers** для dedicated hosting, а PlayFab + Azure — для мета-сервисов. Рекомендуемая основа серверного кода остаётся отдельным C# Match Server с нашим `MatchService`/LeoECS Lite; актуальное уточнение — [PlayFab_Azure_Stack.md](PlayFab_Azure_Stack.md). Конкретный transport и версии SDK фиксируются только после spike; Unity остаётся клиентом и инструментом authoring.

Если принципиально нужен готовый Unity networking package и Unity headless server, рекомендую **Mirror** как запасной вариант: бесплатный MIT-код, без оплаты за CCU, собственный hosting. **NGO + Unity Transport** также подходит для dedicated topology. FishNet достоин рассмотрения, но его преимущества в prediction не решают главную задачу нашего раундового матча. Fusion 2 технически подходит в Server Mode и выгоден на бесплатном старте, однако добавляет Photon-платёж при росте поверх hosting нашей симуляции.

Выбор основан на нынешней архитектуре и механике, а не на универсальном рейтинге библиотек. До миграции нужно проверить C# процесс и мобильный transport на нагрузке. Собственный сервер не будет автоматически дешевле по сумме человеко-часов: мы берём на себя протокол, эксплуатацию и восстановление. Эти затраты надо сравнивать с реально используемыми возможностями готового SDK.

Это историческое сравнение не выбирает Mirror, PUN, NGO или иной transport. Оно не оплачивает услуги и не меняет runtime. Решение о MPS/Azure не означает импорт SDK или live provisioning. Требования к authority, дедлайнам и восстановлению — [Server_Authority_Architecture.md](Server_Authority_Architecture.md).

## Как трактуется нагрузка

Для запаса считаем **20 / 50 / 100 одновременно играющих людей (CCU)**, а не столько зарегистрированных тестировщиков. Сто участников, играющих по очереди, создают меньшую нагрузку.

При PvP это 10 / 25 / 50 одновременных матчей. Если каждый играет с ботом — до 20 / 50 / 100. Дополнительно живут матчи отключившихся игроков: сервер не должен прекращать их только ради снижения нагрузки. Нагрузка считается по матчам, боевым сущностям, snapshot traffic и сохранению решений.

Большая аудитория означает несколько разных величин: MAU/DAU для профилей, peak CCU для резерва машин, average CCU для ежемесячного трафика, матчей/секунду для allocator и результатов. Нельзя вывести цену для миллиона установок без этих чисел.

## Почему текущий проект допускает обычный C# сервер

По текущим исходникам проверено:

- `Match.Domain`, `Simulation`, `Contracts`, `Match.Network.Core` имеют `noEngineReferences: true`. MatchService и BattleSimulation не используют UnityEngine, PhysX, Unity Time, Mathf или Photon.
- Симуляция содержит собственные движение/раздвижение, попадания, снаряды, зоны, RNG и fixed-step. Серверу не требуется Unity сцена для вычисления этих правил.
- PUN находится в `PhotonMatchTransport`; замена доставки не требует переписывать всю игру или переводить каждый танк в другой ECS.
- ScriptableObject-конфиги остаются Unity authoring: нужно экспортировать versioned data bundle в DTO для сервера. Иерархии, prefabs, материалы и VFX остаются клиентскими.
- Core пока не оформлен отдельным .NET server solution; отсутствуют durable match lifecycle, полноценные checkpoints и server scheduler. Переносимость исходников подтверждена чтением, работоспособность отдельного процесса ещё не проверена.

Основные пути: `Assets/TankDraft/Runtime/Match/Domain`, `Battle/Simulation`, `Shared/Contracts`, `Match/Networking/Core`, `Match/Networking/Transport`. На момент исследования Editor: Unity 6000.3.10f1, MainMenu в Edit Mode, scene clean, compiling false. Исследование не запускало новый сетевой benchmark.

## Сравнение применительно к нашей игре

Все строки с dedicated server предполагают, что процесс принадлежит нам и работает без клиентов. Server authority в библиотеке не означает, что она знает правила покупки танка или сохраняет матч после потери процесса.

| Стек | Что получаем | Что остаётся реализовать | Оценка для TankDraft |
|---|---|---|---|
| ASP.NET Core / C# + WebSocket | Стандартный серверный runtime, HTTP/TLS, двусторонний transport; прямое использование нашего чистого core | Матчевый протокол, sync/recovery, clocks, persistence, auth integration, matchmaking и эксплуатация | Основная рекомендация: совпадает с уже выбранной моделью данных и не требует Unity runtime на сервере |
| Mirror + Unity dedicated | Unity transport abstraction, сообщения, RPC/SyncVars, object replication, interpolation, сетевые диагностики | Правила, server persistence, restore, metagame backend, размещение | Лучший запасной Unity-вариант; можно использовать сообщения поверх ECS без NetworkIdentity на каждой пуле |
| NGO + Unity Transport + Unity dedicated | Официальный GameObject netcode, сообщения/RPC, network objects, инструменты Unity | Те же долговечные игровые правила и backend; dedicated hosting | Подходит; разумен при предпочтении официального Unity workflow, не требует Relay для прямого подключения к серверу |
| FishNet + Unity dedicated | Серверная сеть, replication и средства prediction; бесплатная база | Durable match/backend/hosting и наши правила | Подходит, но дополнительные real-time возможности пока не причина менять архитектуру |
| Fusion 2 Server Mode | Tick-based replication, prediction, interpolation, Photon connectivity | Размещение доверенного simulation host, durable match/rewards и правила | Сильный пакет для real-time Unity; бесплатный старт привлекателен, долгосрочная доплата должна оправдываться используемыми функциями |
| Netcode for Entities | Сеть и prediction для Unity DOTS/Entities | Переход/адаптация к Unity Entities, domain persistence и hosting | Не рекомендую: это другой ECS, а у нас принят LeoECS Lite |
| Nakama | Готовый game backend: аккаунты, social/matchmaking/storage и authoritative handlers | Наши правила/recovery; C# simulation требует внешнего worker или переноса на другой язык | Кандидат для большой меты, не прямой контейнер для нынешнего LeoECS |
| Colyseus | Authoritative room framework, schema synchronization и клиентские SDK | Перенос/внешний вызов C# логики, долговечное восстановление и hosting | Полезен для TS/Node проектов, здесь добавляет второй серверный стек |
| PUN 2 player authority / Relay-host / Distributed Authority у клиентов | Доставка/синхронизация с участием игроков | Доверенная независимая симуляция отсутствует в выбранной схеме | Не отвечает требованиям матча без обоих клиентов |

Факты о возможностях и лицензиях: [Mirror repository](https://github.com/MirrorNetworking/Mirror), [Mirror messages](https://mirror-networking.gitbook.io/docs/manual/guides/communications/network-messages), [Unity netcode packages](https://docs.unity.com/en-us/multiplayer/netcode/netcode), [FishNet](https://github.com/FirstGearGames/FishNet), [Fusion dedicated topology](https://doc.photonengine.com/fusion/v2/concepts-and-patterns/dedicated-server-overview), [Nakama authoritative handlers](https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/), [Colyseus](https://docs.colyseus.io/). Последний столбец — наша оценка соответствия, а не заявление производителей.

В сравнении не предполагается «один Unity процесс на один матч»: несколько матчей можно разместить и в Unity host при соответствующей организации. Преимущество pure C# — отсутствие необходимости загружать engine и более прямое управление lifecycle, а точную разницу CPU/RAM определит измерение. Для примера Fusion официально поддерживает [несколько game sessions в одной Unity instance](https://doc.photonengine.com/fusion/v2/manual/testing-and-tooling/multipeer).

## Что имеется в Unity 6

В Unity есть пакеты NGO, Netcode for Entities, Unity Transport и Multiplayer Services SDK. Это несколько уровней, а не встроенный бесплатный backend всей игры. В текущем руководстве Unity 6000.3 опубликован NGO 2.13.2; при установке закрепляем проверенную совместимую версию, не плавающий latest. [Unity 6.3 package reference](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.netcode.gameobjects.html)

NGO опубликован под MIT; само использование пакета не вводит плату за каждое подключение. Hosting и платные UGS-сервисы считаются отдельно. Mirror также MIT и не имеет CCU-платежей. У FishNet своя лицензия: бесплатное и royalty-free использование для игр с указанными условиями; это не MIT. Некоторые функции вынесены в Pro, например collider rollback lag compensation. Для наших приказов/автобоя Pro сейчас не обоснован. [NGO license](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop/LICENSE.md), [Mirror](https://github.com/MirrorNetworking/Mirror), [FishNet license](https://github.com/FirstGearGames/FishNet/blob/main/LICENSE.md), [FishNet Pro](https://fish-networking.gitbook.io/docs/overview/readme/pro-projects-and-support)

Relay пересылает сообщения, а не выполняет нашу симуляцию. Distributed Authority распределяет ответственность за объекты между клиентами. Обе модели полезны для других игр, но сами не дают нужного доверенного матча, идущего при нуле игроков. NGO с нашим dedicated server — отдельная, подходящая схема. [Unity Relay](https://docs.unity.com/relay/relay-servers), [Unity authority topology](https://docs.unity.com/en-us/multiplayer/game-types/co-op-games)

Cloud Code полезен для серверных функций меты и асинхронных действий. Но C# invocation ограничен 15 секундами и сохранение памяти между вызовами не гарантировано. Это не готовый непрерывный host для нашего боя. Можно спроектировать chunked/serverless исполнение, но для нашей live simulation это отдельная сложность без доказанной выгоды. [Cloud Code limits](https://docs.unity.com/en-us/cloud-code/modules/reference/limits)

Также нельзя опираться на старые гайды «бесплатно начнём на Unity Multiplay»: Unity завершила прямую поддержку своего Multiplay Hosting 31 марта 2026 года, передав лицензию Rocket Science Group. Это касается hosting, а не отмены NGO или всех UGS. Новую инфраструктуру выбираем по действующим условиям конкретного провайдера. [Unity notice](https://docs.unity.com/multiplay-hosting/guides/game-engine-best-practices)

## Актуальные платежи SDK и облачной доставки

Цены проверены 2026-09-13. Валюты не конвертировались, налоги/условия аккаунта надо учитывать отдельно. Ниже нет стоимости аренды нашей симуляции, БД, арта/CDN, SMS/email, поддержки и разработки.

| Вариант | Цена программного стека / доставки | Существенное условие |
|---|---|---|
| .NET / ASP.NET Core | $0 лицензия; нет CCU тарифа | Hosting и инженерная работа оплачиваются отдельно. [Microsoft](https://dotnet.microsoft.com/en-us/platform/free) |
| Mirror / NGO | $0 за базовые пакеты, нет CCU тарифа SDK | Unity server и backend размещаем сами; условия самого Unity Editor отдельно |
| FishNet Free | $0 за базу | Pro необязателен; платную поддержку не включаем |
| Fusion 100 CCU | $0, одно приложение, 0.3 TB/месяц | Жёсткий лимит; не безлимитный hosting игры |
| Fusion 500 / 1,000 / 2,000 CCU Public | $125 / $250 / $500 в месяц | Лимиты трафика 1.5 / 3 / 6 TB, далее overage |
| Fusion Premium | $0.50/CCU, минимум $1,000/месяц | Расчёт по пикам регионов; при 10,000 тарифицируемых CCU ориентир $5,000 только Photon |
| Unity Relay | Первые 50 среднемесячных CCU бесплатно; далее $0.16 за дополнительный средний CCU | Bandwidth: 3 GiB/CCU до 150 GiB/месяц бесплатно; дальше $0.09/GiB US/EU, $0.16 Asia/Australia |
| Nakama self-hosted | Open-source core без hosting-подписки | Сервер/БД и эксплуатация наши; managed/Enterprise — отдельное предложение |

Источники цен: [Photon Fusion](https://www.photonengine.com/fusion/pricing), [правила Photon billing](https://doc.photonengine.com/photon/current/pricing), [UGS pricing](https://unity.com/products/gaming-services/pricing), [Nakama self-host](https://heroiclabs.com/docs/nakama/getting-started/install/docker/), [Heroic Cloud](https://heroiclabs.com/pricing/). Цена Nakama Cloud не извлеклась из интерактивного калькулятора, поэтому выдуманная сумма не приводится; $600 на странице относится к Satori, не к минимальному Nakama.

Photon считает peak CCU как сумму пиков по регионам, Unity Relay — среднемесячный CCU. Например, 100 человек по 2 часа в 10 дней месяца дают 2.78 среднего CCU для 30-дневного расчёта, но peak равен 100. Бесплатность CCU не гарантирует бесплатность всего трафика. У Photon учитывается входящий и исходящий cloud traffic, это не то же самое, что только egress нашего VPS.

Fusion Server Mode требует **и Photon Cloud, и отдельно размещённого game server**. Бесплатные 100 CCU не включают его CPU/RAM/БД. Для теста ровно на границе тарифа дополнительно проверить фактический учёт соединений выбранной topology в Dashboard; лимит `PlayerCount` комнаты не является определением биллинга. [Fusion hosting](https://doc.photonengine.com/fusion/v2/concepts-and-patterns/dedicated-server-overview)

PUN Plugins не являются дешёвым дополнением обычного AppId: Enterprise имеет отдельные коммерческие условия; самостоятельный Photon Server также лицензируется отдельно, текущая документация связывает покупку лицензий с Industries Circle. Не ставим этот вариант в бюджет €10–20 без подтверждённого предложения поставщика. [Photon deployment/licensing](https://doc.photonengine.com/server/v5/getting-started/onpremises-or-saas)

## Конкретный состав рекомендуемого стека

| Слой | Предлагаемый компонент | Назначение |
|---|---|---|
| Клиент | Unity 6 + текущие view/pools/LeoECS contracts | Отображение подтверждённого состояния, ввод решений |
| WebSocket клиент | NativeWebSocket за нашим transport interface | Мобильный WSS; AOT/background/reconnect проверяются на устройствах |
| Сервер | Поддерживаемый .NET LTS + ASP.NET Core/Kestrel | HTTPS API, WSS, authentication middleware, ограничения запросов, match workers |
| Игровое ядро | Наши Contracts + Match.Domain + Simulation + Network.Core | Один комплект правил для серверного исполнения и локальных проверок |
| Формат сообщений | Сначала существующий codec за versioned interface; MessagePack-CSharp при необходимости | Компактные DTO, одинаковые схемы и ограниченные размеры; без сериализации Unity иерархий |
| Хранилище | См. [PlayFab_Azure_Stack.md](PlayFab_Azure_Stack.md) | Azure storage proposal для state/receipts/results; PostgreSQL остаётся примером альтернативного SQL hosting |
| Эксплуатация теста | PlayFab Multiplayer Servers | Dedicated hosting для server spike; конкретные лимиты, образ и конфигурация проверяются отдельно |
| Метрики | Стандартные .NET metrics/structured logs, последующее подключение мониторинга | CPU/RAM, deadline lag, ACK latency, queue/recovery/traffic |

NativeWebSocket заявляет поддержку Unity, Android/iOS/WebGL; конкретная версия с нашим Unity 6 и IL2CPP ещё не проверена. MessagePack имеет поддержку Unity/AOT, но тоже требует закрепления версии и проверки схем. Npgsql остаётся стандартным .NET доступом к PostgreSQL для альтернативного SQL hosting, но не является текущим Azure storage proposal. Это готовые компоненты, а не разработка TCP/TLS/формата БД с нуля. [NativeWebSocket](https://github.com/endel/NativeWebSocket), [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp), [Npgsql](https://www.npgsql.org/doc/index.html)

Не вводить MessagePack только ради названия пакета: сначала замерить текущий бинарный codec, его безопасность, payload и трафик. Не нужен отдельный Redis/Kubernetes/Kafka на первом стенде. Auth, matchmaking, профиль и settlement выделяются логически; выбранная PlayFab + Azure основа описана в [PlayFab_Azure_Stack.md](PlayFab_Azure_Stack.md) и не должна становиться вторым владельцем боевого состояния. Пароли/криптографию самостоятельно не изобретать.

WebSocket не устраняет задержки: TCP может удерживать свежие данные за потерянным сегментом. Для наших редких решений и управляемого потока состояния это разумный старт, но 150–300 ms RTT с потерями нужно реально проверить. При неудовлетворительном результате сравниваем transport adapter с LiteNetLib для snapshot traffic; целостность матча, TLS/auth, приоритеты сообщений и защита входа требуют отдельного решения, не автоматического переноса свойств WSS на UDP. [LiteNetLib](https://github.com/RevenantX/LiteNetLib)

Nakama полезнее рассматривать для аккаунтов/социальной меты, если стоимость собственной мета-разработки окажется выше интеграции. Его серверные runtime — Go, TypeScript/JavaScript и Lua; Unity C# SDK не означает возможность загрузить в Nakama наш C# BattleSimulation. Внешний worker возможен, но это уже два backend-компонента. [Nakama runtime](https://heroiclabs.com/docs/nakama/server-framework/introduction/)

## Сколько стоят машины для теста

Ниже — исторический расчёт самостоятельного VPS как альтернативы выбранному для проверки PlayFab MPS. Он не является текущей рекомендацией или сметой PlayFab. На одном VPS можно проверить отключение клиентов и рестарт приложения, но нельзя доказать устойчивость к потере самого физического узла/диска.

### Недорогой ежемесячный стенд

В официальном обновлении Hetzner от 15 июня 2026 цены CX выросли. Использованы новые EU-цены, не старые рекламные €3–4:

| Машина | Ресурсы | VPS/месяц | IPv4 | Backup 20% | Итого без VAT |
|---|---|---:|---:|---:|---:|
| CX23 | 2 shared vCPU / 4 GB | €5.49 | €0.50 | €1.10 | **€7.09** |
| CX33 | 4 shared vCPU / 8 GB | €8.49 | €0.50 | €1.70 | **€10.69** |
| CX43 | 8 shared vCPU / 16 GB | €15.99 | €0.50 | €3.20 | **€19.69** |

Расчёт: `VPS × 1.20 + IPv4`, округление до цента. Источники: [новые тарифы](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/), [конфигурации](https://www.hetzner.com/cloud/cost-optimized/), [IPv4](https://docs.hetzner.com/cloud/servers/primary-ips/overview/), [backup billing](https://docs.hetzner.com/cloud/billing/faq/).

У Cost-Optimized ограниченная доступность. Полученная публичная страница показывает `not available`, поэтому эти предложения — ценовой ориентир при наличии, не обещание заказать их сегодня. Нужно проверить Console выбранного региона и возможность оплаты перед размещением. ARM CAX не подменять автоматически вместо x86: совместимость и повторяемость server simulation проверяем отдельно.

В EU для CX указан включённый исходящий трафик 20 TB; у других регионов/семейств лимиты отличаются. Это полезно для тестов с snapshot stream. [Hetzner traffic](https://docs.hetzner.com/robot/general/traffic/)

Домен, отдельное хранение DB dumps/WAL, почта, нагрузочная машина и дополнительные monitoring services в таблицу не включены. VM backup не является синхронной replica и не гарантирует сохранность каждого Accepted при потере диска. Для закрытых тестов без реальных покупок один VPS допустим; требуемую release durability проверяем на отдельной конфигурации.

### Альтернатива и оплата только времени тестов

DigitalOcean Basic Regular: 2 vCPU/4 GiB — $24/месяц или $0.03571/час; 4 vCPU/8 GiB — $48/месяц или $0.07143/час. При 100 часах существования VM вычислительная часть составит **$3.57 / $7.14**. Трафик входит в тариф в установленных объёмах; при короткой жизни VM доступный allowance нужно сверить по правилам биллинга. [DigitalOcean pricing](https://www.digitalocean.com/pricing/droplets)

Цена за ограниченное число часов предполагает создание/удаление тестовой VM с сохранёнными снаружи нужными данными. Просто выключить процесс/сервер обычно недостаточно, чтобы исчезла оплата выделенного ресурса. Persistent disks, IP, backup/snapshots могут продолжать тарифицироваться; переносить эту экономию на активный релизный матч нельзя. Ничего не удаляется автоматически этим исследованием.

Для локального server + нескольких клиентов аренда равна $0; остаются имеющееся оборудование/электричество. Для 100 внешних игроков удобнее VPS: работа матча не должна зависеть от сна рабочего компьютера, NAT и домашнего upload. Бесплатные trial credits полезны, но не являются основой постоянного бюджета.

### План проверки вместимости

| Уровень | Целевая нагрузка | Стартовая конфигурация для измерения |
|---|---|---|
| 20 людей | 10 PvP / до 20 bot matches | 2 vCPU/4 GB |
| 50 людей | 25 PvP / до 50 bot matches | 4 vCPU/8 GB |
| 100 людей | 50 PvP / до 100 bot matches, плюс offline-продолжение | 4 vCPU/8 GB; при нехватке проверить 8 vCPU/16 GB |

Это **план эксперимента**, не уже измеренная ёмкость. Если базовый тест не помещается в выбранный бюджет, сначала профилируем simulation/serialization/DB и устраняем bottleneck, затем пересчитываем смету. Цена «€10–20 за 100» до этого остаётся целью, не гарантией. Общая стоимость выбранного тестового периода включает также backup, трафик и внешние сервисы.

## Что изменится при росте аудитории

Для .NET/Mirror/NGO/FishNet нет отдельного счётчика лицензионной платы за каждую тысячу клиентов SDK. Есть реальные расходы на compute, БД, трафик, регионы, резерв и сопровождение. Отсутствие CCU тарифа не означает бесплатное или автоматическое масштабирование.

Предлагаемое развитие: много match workers в процессе → несколько процессов/машин → размещение по регионам с владельцем каждого `matchId`. Выбранный Azure storage proposal и observability постепенно отделяются от game worker; PostgreSQL и его backup/failover остаются альтернативным self-hosted SQL-вариантом. Match lease/fencing, durable inputs и schema/version compatibility проектируются с начала, даже если тестовый deployment один. Auth/profile/leaderboard/cache не должны блокировать simulation tick медленным запросом.

Ориентир расчёта трафика нашего сервера:

`egressBytes = средний онлайн × средние байты/секунду на клиента × длительность`

Пример, **не замер игры**: при средних 20,000 байт/с на клиента, включающих все фазы, и 30 днях получаем:

| Средний CCU | Исходящий трафик |
|---:|---:|
| 100 | 5.184 TB |
| 1,000 | 51.84 TB |
| 10,000 | 518.4 TB |

Использованы десятичные TB, не GiB; wire overhead дополнительно измеряется. Для Photon/Relay нужно пересчитать все тарифицируемые участки доставки. Компактные DTO, независимые simulation/network/render частоты и замена устаревших snapshot уменьшают расходы. Поэтому нет оснований обещать точную цену массового релиза без замера traffic и плотности матчей.

Формула полной стоимости: `SDK/CCU + worker-hours + DB/replication + egress/overage + storage/backups + gateway/protection/monitoring + engineering/operations`. У готового провайдера часть эксплуатации оплачивается тарифом; у self-host эта работа остаётся нам. Это главный компромисс собственного решения.

## Проверка перед фиксацией стека

1. Собрать наши pure C# assemblies в отдельный server process; экспортировать конфиги и провести одинаковые сценарии с тем же результатом, что в текущем core.
2. Соединить два реальных Unity-клиента через WSS. Проверить выбор, бой, отключение на два раунда, current-state resume и финал.
3. Добавить persistent storage и проверить kill/restart после Accepted, в полёте снаряда и перед выдачей результата. Переключение transport не считается решением persistence.
4. Запустить лёгкие протокольные клиенты на нагрузку 20/50/100 и bot/offline сценарии. Не поднимать 100 Unity Editors ради имитации 100 подключений. Клиенты нагрузки должны посылать реальные команды и читать snapshot, а не держать пустые sockets.
5. Замерить CPU/RAM, p95/p99 шага и durable ACK, deadline lag, recovery time, входящий/исходящий traffic, DB rate. Повторить с максимальными армиями/зонами и плохой сетью; генератор нагрузки не должен незаметно съедать CPU измеряемого сервера.
6. Проверить Android/iOS IL2CPP, background/foreground, Wi-Fi/LTE, app force-close, 30 FPS и RTT 50/150/300 ms с потерями. Только после этого утверждать версии пакетов и реальный бюджет на 100 человек.

Если WSS/core server проходит критерии — выбираем его и убираем PUN из production dependency после миграции. Если главная проблема в TCP delivery — сравниваем альтернативный транспорт при том же domain. Если нужна большая доля Unity object replication/engine physics — выбираем Mirror или NGO dedicated, сохраняя серверные правила и БД. Не устанавливать сразу все сетевые SDK «на всякий случай».

Исследование и исторические расчёты завершены. Проверены первичные документы/прайсы, переносимость текущего core и арифметика альтернативной VPS-сметы. Владелец выбрал попробовать PlayFab MPS для dedicated hosting и PlayFab + Azure для меты; см. [PlayFab_Azure_Stack.md](PlayFab_Azure_Stack.md). Производительность MPS, выбранный transport/SDK на устройствах, доступность облачных ресурсов для аккаунта и production SLA ещё не проверены.
