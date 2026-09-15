# Данные игрока и экономика в PlayFab
Status: accepted; temporary Legacy QA adapter selected 2026-09-15; V2 deferred
Last reviewed: 2026-09-15

15.09 дополнение надёжности: [Economy_Reliability.md](Economy_Reliability.md) — server-verified покупки, durable fulfilment до confirm, CAS и запрет reset при ошибке загрузки; Legacy QA награды имеют локальный audit/одну компенсацию. Реальные покупки ещё не реализованы. Для Legacy Google есть готовый ValidateGooglePlayPurchase, но совместимость Unity IAP/receipt и account-bound recovery требует sandbox. Таблица V2 ниже остаётся целевым вариантом, billing не включён.

**Актуальное исключение 15.09:** владелец выбрал Legacy Catalog/Inventory/Currency на время QA. Профиль Entity Objects/CAS сохранён. [PlayFab_Legacy_QA.md](PlayFab_Legacy_QA.md) описывает применённый объём и тесты. Таблица V2 ниже — целевое направление, не текущий QA provider. Включение V2/миграция потребуют отдельного плана; никаких двойных кошельков.

Владелец утвердил уточнение после разбора PlayerData/Inventory/TitleData/Economy: PlayFab остаётся основным хранилищем пользовательских данных; Azure сейчас не возвращаем. Это заменяет прежнее обязательное размещение custom progression в Azure Tables. Смена хранилища игрока не меняет принятую dedicated authority боя. Вопрос о PUN 2 является анализом альтернативы, не поручением мигрировать.

## Распределение данных

| Данные | Источник истины | Правило |
|---|---|---|
| Прогресс, арена, обучение, выбранная колода | PlayFab Entity Objects на title_player_account | Версия схемы, контроль ExpectedProfileVersion; игровой прогресс пишет доверенный backend. Перед подключением обязательно проверить запрет записи с player token |
| Владение техникой/картами/расходниками, валюты | Economy V2 Inventory | Не создавать второй кошелёк в профиле; видимые клиенту значения вне Inventory — только кэш/проекция |
| Уровень принадлежащей игроку техники | Свойства экземпляра/stack Inventory | Связанные списания и изменение уровня выполнять одной поддерживаемой Inventory-транзакцией в той же коллекции |
| Товары, наборы, цены, скидки | Economy V2 Catalog + Stores | Сервер проверяет товар, StoreId, цену, требования и лимиты; клиент передаёт намерение |
| Боевой баланс, правила драфта, гейты | TitleData / Internal TitleData | Единый authored export, проверяемый content version; неизменный набор данных на матч |
| Реальные покупки | Unity IAP + PlayFab Economy V2 marketplace redemption | Проверка receipt/token, account/product binding, повтор, восстановление и возврат требуют отдельной реализации и sandbox QA |

Inventory целевого V2 — часть Economy V2; для текущего QA используется разрешённый владельцем Legacy adapter. Обычный UserData с клиентской записью допустим только для недоверенных предпочтений; серверные игровые права туда не переносим. Entity Objects выбраны для новых профильных данных, а не как неограниченная база всех событий. Размер, количество объектов, API policy и квоты проверяются для B16D9 перед реализацией.

## Что делает наш backend

Unity отправляет команду вроде Equip/Upgrade/Claim, а не новый баланс или победителя. Наш C# backend проверяет identity, владение, стоимость, состояние и право на операцию, затем обращается к PlayFab. Для первого среза этот код может выполняться на Edgegap без Azure Functions; когда deployment выключен, наши custom commands недоступны. Постоянная доступность meta API — отдельная эксплуатационная задача. Привилегированные credentials никогда не выдаются клиенту.

Entity Objects используют ExpectedProfileVersion, Inventory — свой механизм concurrency/ETag. Это разные области согласованности: общего атомарного commit между профилем, Inventory и двумя игроками не предполагаем.

Для награды нужен постоянный учёт результата и стадий обработки: результат → зафиксированная операция → Inventory → прогресс → завершение. Одна logical operation сохраняет тот же id при retries; при timeout нельзя создавать новый grant. IdempotencyId Economy V2 хранится 14 дней, поэтому не заменяет постоянный учёт обработанных результатов. Конкретная схема хранения журнала, восстановления незавершённых операций и его ограничения ещё проектируется; её нельзя объявлять готовой лишь потому, что профиль сохраняется в PlayFab.

## Отличие от сохранения боя

В Entity Objects не пишем позиции/снаряды на каждом тике. Текущий матч исполняется на доверенном Edgegap server. Ранее принятое временное правило сохраняется: при потере контейнера незавершённый матч void без награды/потерь. Восстановление самого боя после потери машины требует отдельного checkpoint/journal/ownership решения. Отдельная Azure-подписка не нужна просто для хранения профиля в PlayFab.

## Первый вертикальный срез

1. Версионированный профиль в Entity Objects и серверная проверка прав записи.
2. Небольшой Economy V2 каталог, inventory и отображение владения в UI.
3. Выбор доступной колоды → server validation → бой.
4. Постоянно зафиксированный результат → однократная награда → прогресс.
5. Повторный вход и fault QA до/после внешнего вызова, lost ACK, concurrent requests, запрет клиентского grant/SetObjects.

Это утверждённое направление и порядок реализации, не evidence работающей экономики. Live title/catalog/policy, миграции, реальные покупки и платные услуги этим документом не включаются.

## Основание

- [PlayFab Player Data: Entity Objects рекомендованы новым проектам](https://learn.microsoft.com/en-us/xbox/playfab/player-progression/player-data/).
- [SetObjects: ExpectedProfileVersion](https://learn.microsoft.com/en-us/rest/api/playfab/data/object/set-objects?view=playfab-rest).
- [Inventory transactions: одна коллекция](https://learn.microsoft.com/en-us/rest/api/playfab/economy/inventory/execute-inventory-operations?view=playfab-rest).
- [Economy V2 retries и окно 14 дней](https://learn.microsoft.com/en-us/gaming/playfab/economy-monetization/economy-v2/tutorials/idempotent-transactions-and-retries).
- [TitleData: кэш, не оперативное состояние матча](https://learn.microsoft.com/en-us/xbox/playfab/live-service-management/game-configuration/titledata/).
- Reference read-only audit: `U:/UNITY_PROJECTS/chibi_arena/PlayFabAzure/BackEnd/Economy/PurchaseStoreItemService.cs` использует Economy V2; `Progression/ProgressionStore.cs` использует Azure Tables. Переносим контракты, не обязательность Azure и не неподтверждённую надёжность reference live backend.
