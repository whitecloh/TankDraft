# Временная Legacy экономика для PC QA
Status: R3.1 Legacy PC acceptance PASS; R3.2 result/reward persistence next
Last reviewed: 2026-09-15

Продолжение15.09: [Match_Result_Settlement.md](Match_Result_Settlement.md) добавляет отдельный opt-in LegacyTestRewards с постоянным журналом:1CO за тестовый матч, максимум10выдач на аккаунт. Историческое «наград нет/балансы0» ниже относится к завершению R3.1, а не к последующему R3.2. Тариф и клиентские write-deny не менялись.

## Решение владельца

15.09 владелец выбрал временные Legacy Catalog, Inventory и Currency вместо Economy V2. Это заменяет прежнее «Legacy не вводим». PlayFab остаётся хранилищем, профиль остаётся в Entity Objects; Fusion dedicated/.NET authority и отсутствие Azure сохраняются. Обращение по V2 billing не отправляем. Тариф и платёжные данные не меняем.

## Контракт

Явный режим `PlayFabLegacyClosedQa` выбирает `PlayFabLegacyPlayerDataStore`. `PlayFabClosedQa` сохраняет V2 адаптер для будущего отдельного перехода; смешанные настройки отвергаются. Сетевые ProfileGet/ProfileSave и UI не знают о типе Inventory. `PlayFabEntityProfileStore` общий для обоих вариантов, использует server-resolved identity и ExpectedProfileVersion.

Legacy `/Server/GetUserInventory` возвращает экземпляры и VirtualCurrency. ItemId + CatalogVersion сопоставляются с authored ContentId. Неизвестные/чужие версии не становятся владением; просроченные/исчерпанные экземпляры не учитываются. Некорректные/повторные экземпляры и балансы вне 0..Int32.MaxValue отвергаются. `SourceVersion=legacy:<hash>` — fingerprint прочитанного владения и балансов, **не provider ETag/CAS**. Деньги и инвентарь не копируются в профиль.

R3.1 не выдаёт награды и не списывает валюту. У Legacy нет используемой нами гарантии идемпотентного изменения баланса: в R3.2 нельзя после потерянного ответа безусловно повторять AddUserVirtualCurrency. Durable журнал результата, состояния выдачи и обработка неопределённого результата остаются обязательными; локальный marker QA provisioning не заменяет эту архитектуру.

## B16D9: применённый QA объём

- Каталог `tankdraft-qa-v1`: 24 authored ContentId, durable/non-tradable, без цен и real-money продуктов. Существующие конфликтующие записи скрипт не перезаписывает. Первый каталог может стать default по правилу PlayFab, хотя SetAsDefaultCatalog=false; игровой адаптер и grants всегда задают версию явно.
- Два ранее созданных allowlisted QA игрока: по 9 стартовых карточек из `meta-legacy-provisioning-review.json`, без новых аккаунтов и без начисления валюты.
- CO → coins, GM → gems, EN → energy: начальный депозит и регенерация 0. В настоящем Inventory все три баланса обоих игроков прочитаны как 0.
- ApiPolicy расширена 24 deny-правилами с текущей PolicyVersion/append, прежние правила сохранены: player Entity Objects/Profile policy, V2 Inventory и прямые клиентские Legacy покупки/выдача/списание/trade/redemption/CloudScript. Server API остаются за серверным ключом. Это closed-QA policy, которую нужно отдельно пересмотреть для настоящих покупок и CloudScript.

Секреты и настройки только `%USERPROFILE%/.codex-secrets/TankDraft`. `playfab-legacy-meta.json` содержит маппинги, но не сам ключ. PlayerWritePolicyVerified выставлен после фактических отрицательных проверок, а не после одной успешной записи policy. Manager MetaSettingsPath указывает на этот файл.

## Инструменты и проверки

- `Tools/Backend/test-playfab-legacy-access.ps1`: только read-only preflight, безопасные сводки в Logs/MetaLegacy. Legacy API дали200 при недоступном V2.
- `Tools/Backend/initialize-playfab-legacy-qa.ps1`: по умолчанию review; `-Apply` применяет только описанный QA объём. Before-grant marker хранится приватно, неопределённый запрос автоматически не повторяется. Первый запуск завершился до изменения policy/catalog; повторная read-only проверка подтвердила прежнюю policy13 и пустой каталог, после чего настройка прошла.
- `Tools/Backend/verify-playfab-legacy-policy.ps1`: десять проверок двух существующих player identities. Profile write, profile-policy change, currency grant, direct purchase — NotAuthorizedByTitle; серверная выдача с client ticket — NotAuthenticated. Все10 PASS; server secret в клиенты не передаётся.
- `Backend/TankDraft.MetaLiveProbe --existing-two-qa-legacy`: явный live QA, не часть обычных тестов. На обоих аккаунтах реальные сохранение/чтение/точный повтор, устаревшая версия сервиса и provider CAS, отказ некупленной техники, восстановление состава и revalidation перед очередью — PASS. После проверки ProfileVersion=3, Owned=9, валюты0. Журнал: Logs/MetaLegacy/live-profile.json.
- Domain/provider unit tests33/33; RemoteHost70/70, включая отсутствие неявного переключения Legacy/V2; ServerManager7/7. TRX: Logs/MetaLegacy/legacy-{meta,remote,manager}.trx. Это не доказывает будущую выдачу наград.
- Editor: обычный Fusion Play с живой метой → EquipAsync, чтение нового состава, восстановление исходного, повторное чтение PASS;9owned/балансы0 (Logs/MetaLegacy/editor-equip.txt). Исправлена наследуемая PSModulePath у дочернего Windows PowerShell; вне дочернего процесса среда не меняется. Приёмка выполнена асинхронно; ранний синхронный MCP probe блокировал Editor, после сохранения recovery-копии Editor был перезапущен. Сцена MainMenu чистая, Play остановлен, временные override восстановлены.

## Неуспешные проверки и границы

Финальный независимый standalone: `Logs/FusionServer/game-3a2728132d6349d3a9ba9d430eae84af`, instance7e0173f2b1ca4a289fc466dc65cf3886. Живой Legacy профиль → queue → server Bot → пять раундов → MatchResult4:1 PASS; Connected=true, Connections=1. Использован существующий PC build с новым backend adapter. Это приёмка мета-интеграции в PC матче, не повторная Human-PvP/fault/load/Android приёмка. R3.1 PC срез принят; перечисленные ниже recovery случаи не закрыты этим PASS.

После приёмки drain завершён: manager Stopped/instanceId=null, панель18878 доступна. Клиент завершён; Editor MainMenu вне Play, scene dirty=false/compiling=false, runtime/startScene override очищены. Тариф/карта не менялись, реальные покупки и награды отключены. Commit/push не выполнялись.

- game-2d3d1634b40c478e97ccdc720d475ff2: Legacy-enabled standalone дошёл до round6/2:3, но лимита180s не хватило. Это не полный PASS.
- game-ad260c8fa9864ace938c8130e6c162fd: повтор с новым QA resume journal на сохранённом серверном назначении получил command sequence diverged. Предположение — новая локальная история при старом матче; причина не доказана. Этот случай восстановления остаётся открытым F4; не смешивать с ранее проверенным cold restart, сохраняющим journal.
- game-70912149892e49d3921a18123ce6e4d6: сразу после Editor тот же аккаунт в standalone получил Photon32749 (inactive UserId without rejoin). Для независимого теста создан новый серверный instance. Смена процесса с новым journal при retained Photon actor требует отдельной F4 проверки; restart сервера для QA не считается исправлением recovery.

## Источники

[Legacy items](https://learn.microsoft.com/en-us/xbox/playfab/economy-monetization/economy/items/) — API поддерживаются в режиме исправления ошибок. [GetUserInventory](https://learn.microsoft.com/en-us/rest/api/playfab/server/player-item-management/get-user-inventory?view=playfab-rest) — серверное чтение. [UpdatePolicy](https://learn.microsoft.com/en-us/rest/api/playfab/admin/authentication/update-policy?view=playfab-rest) — append и проверка версии. [UpdateCatalogItems](https://learn.microsoft.com/en-us/rest/api/playfab/admin/title-wide-data-management/update-catalog-items?view=playfab-rest) — добавление и правило default catalog. [Virtual currencies](https://learn.microsoft.com/en-us/rest/api/playfab/admin/title-wide-data-management/add-virtual-currency-types?view=playfab-rest) — коды и ограничения.
