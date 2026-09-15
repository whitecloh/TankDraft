# Доступ Economy V2 для B16D9
Status: V2 deferred by owner; temporary Legacy provider configured separately; no billing changes
Last reviewed: 2026-09-15

Последующее решение владельца: использовать временно Legacy Catalog/Inventory/Currency. Запрос поддержки не отправляем, V2 не включаем. Настроенный QA объём и успешные API-проверки — [PlayFab_Legacy_QA.md](PlayFab_Legacy_QA.md). Наблюдения ниже относятся к V2 и не означают отсутствия последующих Legacy изменений.

## Проверенное состояние

R3.1 реализован локально: [Server_Profile_Collection.md](Server_Profile_Collection.md). Read-only preflight B16D9: разрешение QA identity и GetObjects работают; GetInventoryItems отвечает NotAuthorizedByTitle (1191), GetCatalogConfig — BillingInformationRequired (1266). Это не доказательство необходимости платного тарифа: точное условие включения сервиса ещё неизвестно. IsCatalogEnabled не прочитан.

Первоначальный Azure WAF исчез: Game Manager доступен. Владелец сообщил обычный текст Published Items. Прямая повторная проверка 15.09 с новым токеном Entity.Type=title, Entity.Id=B16D9 снова вернула GetCatalogConfig HTTP400/BillingInformationRequired1266 (Logs/MetaR31/access-recheck.json). Ошибка воспроизведена с корректным типом авторизации; заголовок страницы не доказывает работоспособность API.

Свежий UI B16D9 показывает Development и 3/100K unique players — это фактическое отображение данного аккаунта, отличающееся от общей документации. Форма Edit Billing Information содержит контактные реквизиты (компания, ФИО, страна, адрес, email), без поля карты в просмотренной форме; Upgrade account остаётся отдельной ссылкой. Требование формы: компания должна находиться в поддерживаемой стране. Реальные реквизиты не вводились, форма не сохранялась. Доступность Economy и финансовые последствия сохранения реквизитов ещё не подтверждены.

**Подтверждённая причина:** после загрузки Economy settings для B16D9 отображает: “Title must have a valid payment instrument associated with it in order to enable Economy (V2).” URL: https://developer.playfab.com/en-us/r/t/B16D9/settings/economy/catalog . Это прямое требование платёжного инструмента, а не только контактных реквизитов. Оно не доказывает немедленное списание или обязательный Standard; тариф и защита от расходов требуют отдельного уточнения. Существующий no-charge критерий не ослаблен.

## Официальная документация и границы вывода

- [Development Mode](https://learn.microsoft.com/en-us/xbox/playfab/pricing/development-mode): потребление Development не входит в оплачиваемые метрики. Страница противоречива по лимиту игроков (100 в предупреждении, 1000 в основном тексте/таблице); фактический лимит B16D9 нужно сверять отдельно. Это описание режима не снимает наблюдаемый Economy gate.
- [Account upgrades](https://learn.microsoft.com/en-us/xbox/playfab/pricing/account-upgrades): тариф меняется для аккаунта, Launch — для title. Документ перечисляет PAY-AS-YOU-GO без базовой абонплаты, но его FAQ одновременно говорит об автоматическом Standard при upgrade. Поэтому нельзя рекомендовать Upgrade как гарантированно бесплатное действие без проверки конкретного экрана и условий.
- [GetCatalogConfig](https://learn.microsoft.com/en-us/rest/api/playfab/economy/catalog/get-catalog-config?view=playfab-rest): BillingInformationRequired — документированная ошибка. Её наличие не раскрывает, достаточно ли контактных данных, нужна ли карта или смена тарифа.
- [Foundation onboarding](https://learn.microsoft.com/en-us/xbox/playfab/get-started/foundation-onboarding): указаны Xbox-планы, Entra ID и Partner Center; сведения о preview/миграции датированы прошлым периодом. Не считаем Foundation доступной заменой для существующего мобильного B16D9 без подтверждения условий и отдельного решения владельца.

## Следующее действие

Уточнить у PlayFab возможность включения без платёжного инструмента либо точные финансовые последствия и гарантии ограничения расходов. Ниже готовый запрос; **он не отправлен**. Отправка внешнему получателю требует явного разрешения владельца. Не переносить Inventory в Entity Objects/Legacy и не включать платный тариф как автоматический обход.

После подтверждения доступа без нарушения ограничения на расходы: повторить read-only preflight, перечитать актуальную API policy, подготовить её точный diff и тестовый provisioning. Затем проверить запрет записи от player token, два QA инвентаря и полный PC цикл сохранения армии. R3.1 остаётся незавершённым до этой приёмки.

## Черновик запроса PlayFab Support

Subject: Economy V2 access for Development title B16D9 without enabling paid usage

Hello PlayFab team,

We are integrating Economy V2 Catalog and Inventory for an unreleased mobile game, title B16D9 (Tanks). Our title was created in Development mode. Our server-side read-only preflight can resolve an existing test player's title entity and read Entity Objects successfully. However, Catalog/GetCatalogConfig returns BillingInformationRequired (1266), and Inventory/GetInventoryItems returns NotAuthorizedByTitle (1191).

We need to keep this initial test environment free of charges and have not authorized an account upgrade, title launch, or paid service activation.

Game Manager Economy settings also explicitly requires a valid payment instrument before enabling Economy V2. The title currently displays Development with 3/100K unique players. The separate billing contact form does not establish whether adding contact details or a payment instrument changes the account plan.

Could you please clarify:

1. Can this existing Development title enable Economy V2 Catalog and Inventory without a paid account upgrade or payment instrument? If yes, what exact steps are required?
2. What specific prerequisite produces BillingInformationRequired for this title? Does providing the requested information change the account plan or create any chargeable commitment?
3. What provider-enforced limits prevent charges or paid overage for this configuration, and what happens when each applicable limit is reached?
4. Does Foundation offer a supported path for this existing Android/iOS title without a planned Xbox release? We do not want to misstate platform eligibility or create a replacement title unnecessarily.

Please do not change our plan, launch the title, or enable any paid services. We are requesting clarification only. We can supply sanitized request IDs privately if needed; no credentials are included in this message.
