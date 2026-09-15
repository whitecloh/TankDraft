# R3.1 — серверный профиль, коллекция и армия
Status: R3.1 Legacy profile/collection PC acceptance PASS; R3.2 next
Last reviewed: 2026-09-15

Актуализация 15.09: по решению владельца временно включён PlayFabLegacyClosedQa, каталог tankdraft-qa-v1, CO/GM/EN и девять стартовых карточек двум существующим QA игрокам. Entity Objects профиль и сетевые контракты сохранены. Подробности и действующие проверки — [PlayFab_Legacy_QA.md](PlayFab_Legacy_QA.md). V2 остаётся будущим адаптером; billing, настоящие покупки и награды не включались.

## Реализация

`Backend/TankDraft.Server.Meta` отделяет Entity Objects профиль от provider inventory: текущего Legacy и будущего Economy V2. PlayerMetaService проверяет версию, владение, тип, арену, четыре слота юнитов и три фиксированных слота приказов с уровнями открытия. Пустые слоты приказов допустимы. Инициализация создаёт только tankdraft_profile_v1, если стартовой техникой уже владеет игрок; ничего не выдаёт. Используется фактический ProfileVersion даже при отсутствии этого объекта. Сохранение использует CAS и последнюю операцию/fingerprint для точного повтора. Это не ledger наград R3.2.

Будущий V2 `PlayFabPlayerDataStore` разрешает доверенный classic ID в title_player_account, читает bounded страницы Inventory с проверкой ETag/continuation token и явным сопоставлением ItemId→ContentId. HTTP имеет дедлайн/лимит размера, не следует redirect. Секрет и title EntityToken остаются в backend. Автоматического повторного SetObjects при неопределённом ответе нет.

Fusion ProfileGet/ProfileSave не принимают AccountId, прогресс, баланс или владение от клиента. Private gateway связывает вызов с Photon identity/lobby. RemoteMatchService ограничивает параллелизм и количество вызовов, запрещает смену армии во время очереди/матча; Join перепроверяет профиль и фиксирует immutable состав в назначении. Бой получает этот состав. Нереализованные боевые карточки дают unsupported_battle_loadout, без подмены стандартной армией.

Меню использует async server profile client, существующие authored views и ProfileService. До ACK нет локального Save или смены состава; во время сохранения бой блокируется. Неопределённый запрос повторяется с исходными operationId/version. При 403/409 клиент получает актуальный профиль: старый UI intent не отправляется автоматически на новой версии. Локальный QA доступен только при явном Enabled=false от сервера, не при ошибке сети.

## Настройка

Текущая provisioned-настройка — `PlayFabLegacyClosedQa`. `ManagerOptions.MetaSettingsPath` указывает только на приватный `%USERPROFILE%/.codex-secrets/TankDraft/playfab-legacy-meta.json`; в нём заданы `CatalogVersion=tankdraft-qa-v1`, `LegacyBindings` для authored `ItemId`/`ContentId` и `Currencies` CO/GM/EN. В файле нет server key. Режим выбирает `PlayFabLegacyPlayerDataStore`; смешанные с V2 настройки отвергаются.

`PlayFabEntityProfileStore` и Entity Objects/CAS остаются общими для Legacy и будущего V2: identity разрешает сервер, сохранение использует `ExpectedProfileVersion`, а деньги и инвентарь не копируются в профиль. `PlayerWritePolicyVerified=true` выставлен только после фактических десяти отрицательных проверок player token; клиент не получает server secret. Девять стартовых карточек уже выданы двум существующим allowlisted QA игрокам, CO/GM/EN прочитаны как 0. Полная применённая provision и ограничения closed QA описаны в [PlayFab_Legacy_QA.md](PlayFab_Legacy_QA.md).

Исторический V2 preflight, 59 rules и неприменённые review-манифесты перенесены в [PlayFab_Economy_Access_Check.md](PlayFab_Economy_Access_Check.md). Они не описывают текущую provider-настройку и не разрешают включать billing, покупки или награды.

Повторяемый экспорт: Unity → TankDraft → Backend → Export Meta Rules (MetaServerExport), источники MetaCatalog/NewProfile/ArmyRules. Выход Backend/Content/meta-rules.json/.sha256. Runtime не создаёт SO или UI-иерархию.

Legacy policy добавлена append к текущей версии без перезаписи прежних statements и закрывает player Entity Objects/Profile policy, V2 Inventory и прямые клиентские Legacy покупки, выдачу, списание, trade, redemption и CloudScript. Server API остаётся за серверным ключом. Это closed-QA policy; для настоящих покупок или CloudScript потребуется отдельный пересмотр.

## Проверки и границы

Локальные тесты используют test-only CAS store/HttpMessageHandler. Интеграция выполняется через настоящий loopback HTTP host и Fusion QA peer. В `Logs/MetaLegacy` подтверждены Meta33/33, RemoteHost70/70 и Manager7/7; прежние Client105/105 и Unity PC build reports 15.09 01:42:24/01:42:46 не перезапускались. Десять negative checks с двумя существующими player identities PASS. Live profile для двух аккаунтов PASS: серверные сохранение/чтение, точный повтор, provider CAS, отказ некупленной техники, restore и revalidation перед очередью; ProfileVersion=3, Owned=9, currencies=0.

Обычный Fusion Editor Play после scoped исправления `PSModulePath` PASS: `EquipAsync`, серверное чтение и restore прошли; `Logs/MetaLegacy/editor-equip.txt` фиксирует Owned=9 и Currency=0. Полный standalone с живой метой: queue → server Bot → MatchResult4:1, Connected=true/Connections=1 PASS; Logs/FusionServer/game-3a2728132d6349d3a9ba9d430eae84af. R3.1 PC срез принят. Неуспешные ранние прогоны и recovery случаи сохранены в PlayFab_Legacy_QA.md, этот матч не заменяет Human/fault/Android приёмку.

Открыто: R3.2 результаты/награды, R3.3 прокачка; четыре боевых типа/один приказ расширяются в R4. Pending профильного intent хранится до закрытия клиента; после cold restart читается серверный профиль. Provider/auth outage при загрузке меню, отдельная версия мета-каталога клиента, нагрузка и платёжные сценарии не закрыты этим патчем. Превышение bounded Inventory pages у V2 или размера ответа у Legacy отклоняется явно.

Основания: [SetObjects concurrency](https://learn.microsoft.com/en-us/rest/api/playfab/data/object/set-objects?view=playfab-rest), [Inventory pages/ETag](https://learn.microsoft.com/en-us/rest/api/playfab/economy/inventory/get-inventory-items?view=playfab-rest), [GetPolicy](https://learn.microsoft.com/en-us/rest/api/playfab/admin/authentication/get-policy?view=playfab-rest), [GetGlobalPolicy](https://learn.microsoft.com/en-us/rest/api/playfab/profiles/account-management/get-global-policy?view=playfab-rest).

[GetCatalogConfig](https://learn.microsoft.com/en-us/rest/api/playfab/economy/catalog/get-catalog-config?view=playfab-rest) документирует BillingInformationRequired1266. Общая бесплатность Development Mode не снимает фактически полученный gate отдельного API. Foundation Mode не выбран автоматически: текущая onboarding-документация требует Xbox/Partner Center и отдельного доступа; менять платформу/Title ID вместо решения billing нельзя.
