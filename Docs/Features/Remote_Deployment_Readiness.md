# Готовность первого удалённого запуска
Status: first Edgegap linux-x64 HTTPS readiness deployment verified and terminated; production admission/durability and remote match pending
Last reviewed: 2026-09-13

Входит в [Functional_Release_Plan.md](Functional_Release_Plan.md), этап R1. Актуальное решение и результаты: [Edgegap_Integration.md](Edgegap_Integration.md). Владелец выбрал PlayFab/Azure/Edgegap; Free Tier Edgegap подтверждён через UI. Oracle остановлен на регистрации, MPS не активирован. Ниже сохранены прежняя проверка MPS, QA Linux bundles и анализ альтернатив; они не заменяют текущий Edgegap runbook. Remote PvP пока не принят.

## Проверка условий 13.09.2026

- Известен Title ID B16D9. Предоставленные ранее скриншоты показывали Development и estimated $0; они не доказывают условия будущего MPS/Azure потребления.
- Официальная инструкция [включения MPS](https://learn.microsoft.com/en-us/xbox/playfab/multiplayer/servers/enable-playfab-multiplayer-servers) описывает включение через Game Manager и при завершении требует payment form. На той же странице есть упоминание аккаунтов без payment method; этого недостаточно, чтобы определить доступный режим конкретного аккаунта.
- [Документация billing MPS](https://learn.microsoft.com/en-us/xbox/playfab/multiplayer/servers/billing-for-thunderhead) описывает ограниченное бесплатное evaluation-потребление и тарификацию compute/egress. Публичная страница не дала достаточного подтверждения автоматической остановки всех начисляемых услуг при исчерпании квот для B16D9.
- Пользователь вошёл во встроенном браузере. Live UI подтверждает Tanks / B16D9 / My Game Studio / Development. Другие titles не исследовались и не изменялись.
- На [странице Servers B16D9](https://developer.playfab.com/en-us/r/t/B16D9/multiplayer/server/builds) показан Enable Multiplayer Servers, а не список активных builds. Сообщение: “Add a credit card to enable Multiplayer Servers. You will not be charged until you surpass the data limits below.” Доступна ссылка Add Credit Card. Это прямое подтверждение, что показанный путь разрешает начисления сверх лимитов и не удовлетворяет требованию гарантированных нулевых расходов.
- UI описывает 750 free core hours, совместно используемые VM/подходящими регионами аккаунта, и 10 GB egress, совместно используемые подходящими зонами. Публичная billing-страница формулирует региональные строки иначе; для планирования расхода не складывать их без уточнения у провайдера. Упоминание этих квот не открывает cost gate.

Активация, добавление карты, изменение billing, создание/публикация builds и cloud API не выполнялись. Форму payment не открывали. Дальнейшее продвижение к cloud launch требует подтверждённого альтернативного no-charge режима провайдера либо явного изменения владельцем финансового ограничения. Собственные лимиты проекта могут снижать риск, но не заменяют принудительный отказ провайдера. Сообщение поддержке автоматически не отправляется.

## Технические ворота

| ID | Результат | Текущее состояние |
|---|---|---|
| D1 | No-charge режим подтверждён для всех используемых услуг | Edgegap Free подтверждён; Azure отдельно не подтверждён; MPS закрыт |
| D2 | Linux артефакт, проверенные зависимости/hash, Linux runtime | Edgegap linux-x64 readiness container и HTTPS/core smoke проверены; production host отдельно |
| D3 | Standalone production host/admission с durable state | Нет; новый EdgegapHost — только bounded readiness/core probe; LocalHost/GsdkLocalProbe QA-only |
| D4 | Реальная identity и trusted TLS/client assignment | HTTPS probe проверен; real identity/client assignment ещё нет |
| D5 | Публичный Human match двух независимых клиентов | Не запускался |

После отдельного подтверждения владельца опубликован и проверен bounded Edgegap readiness probe `d409c9b6a7a7`: Free Tier, 0.5 vCPU / 1 GiB, максимум 5 минут, HTTP 8080 с TLS Upgrade. HTTPS health/readiness вернули 200, внутренний бой завершился 4:1 за 5 раундов; `/v1/match=503`, `multiplayerReady=false`. Остановка принята в 19:11:36 UTC 13.09.2026, dashboard подтвердил Terminated. PlayFab/Azure live calls и production player grants не использовались. Секреты не входят в build/manifest/Logs. Полное evidence: [Edgegap_Integration.md](Edgegap_Integration.md).

Локальная среда: Windows, WSL/Docker не устанавливались. OCI archive собран средствами .NET и загружен через crane; фактическая Linux runtime проверка выполнена в Edgegap для readiness host. Исторические QA Linux/ARM64 bundles и будущий production host этим не приняты.

## Историческая QA-упаковка Linux (до выбора Edgegap)

`Tools/Backend/publish-linux-readiness.ps1` публикует существующие LocalHost и GsdkLocalProbe как framework-dependent linux-x64 либо linux-arm64/net10.0 в новый ignored `Logs/BackendLinux/<id>`. По умолчанию сохраняется linux-x64. Это два QA executable, а не объединённый production host. Для Linux restore используются отдельные RID lock-файлы в `Tools/Backend/Locks`, не меняющие исходные lock-файлы SDK/Windows сценариев.

```powershell
# Обычный путь: только проверенные lock-файлы.
./Tools/Backend/publish-linux-readiness.ps1
./Tools/Backend/verify-linux-readiness.ps1 -RunDirectory Logs/BackendLinux/<id>

# Oracle A1: ARM64; verifier также сравнивает фактическую ELF-архитектуру.
./Tools/Backend/publish-linux-readiness.ps1 -RuntimeIdentifier linux-arm64
./Tools/Backend/verify-linux-readiness.ps1 -RunDirectory Logs/BackendLinux/<arm-id> -ExpectedRuntimeIdentifier linux-arm64

# Только осознанное обновление/подготовка RID lock-файлов:
./Tools/Backend/publish-linux-readiness.ps1 -PrepareLocks
# Для ARM64 явно добавить -RuntimeIdentifier linux-arm64.
```

Manifest фиксирует LocalOnly, DeploymentAllowed=false, content checksum, фактические пакеты и хэши файлов. Verifier проверяет целостность относительно manifest и ожидаемый состав; он не является цифровой подписью доверенного издателя и не защищает от согласованной подмены самих файлов вместе с manifest. Отдельный managed guard запускается на Windows; никакие Linux/native/remote runtime проверки этим не заявляются.

Oracle ARM64 preparation 13.09.2026: locked publish `Logs/BackendLinux/ce21004ef68548caa12736e825f7f8f7`, 54 артефакта / 8 308 872 байта. Verifier PASS: manifest/deps linux-arm64, SQLite и apphost ELF machine 183. Повторный x64 locked publish `Logs/BackendLinux/d3c47cc1d1c74db9a1f8cacde48561af`, 54 артефакта / 8 183 284 байта, machine 62. Отклонены неверный ExpectedRuntimeIdentifier и подмена ELF machine SQLite с пересчитанным artifact hash. ARM64 managed entrypoint несовместим с локальным Windows x64 runtime; финальный publisher явно пропускает этот guard для ARM64, а каждый отказ реально запускаемого x64 guard остаётся ошибкой. Linux ARM64 execution пока не выполнен.

Проверено 13.09.2026: финальный обычный locked publish `Logs/BackendLinux/cbd4d12c420f450cb665c5ad5272088c`, 54 артефакта / 8 183 284 байта; verifier PASS, content SHA-256 и `libe_sqlite3.so` присутствуют и проверяются отдельно для обоих executable. Managed GSDK guard: 12 проверок PASS. На временных копиях отклонены отсутствие lock, неверный RID, подмена probe content даже с обновлёнными artifact hashes и отсутствие native SQLite. Дополнительно полный verifier отклонил четыре listener-конфига с подменёнными URI (userinfo/внешний host, query, wildcard host, привилегированный порт) при пересчитанных artifact hashes: `Logs/BackendLinuxChecks/b641b2bb655c471e81a96ffb6d51ce97`.

На этапе Oracle следующими оставались Linux ARM64 execution и standalone host. Этот приоритет заменён Edgegap linux-x64 container: [Edgegap_Integration.md](Edgegap_Integration.md). Исторические QA bundles остаются DeploymentAllowed=false и не публикуются; их проверка не доказывает состояние нового host.

## Бесплатные альтернативы для тестов — проверка 13.09.2026

Владелец уточнил цель: 10–20 одновременных матчей (20–40 человеческих подключений). Запрет расходов не отменён, провайдер автоматически не менялся. Закрытый no-charge gate MPS не означает отсутствия бесплатного удалённого compute у других провайдеров.

| Вариант | Проверенное публичное предложение | Условия и ограничения |
|---|---|---|
| Oracle OCI Always Free | ARM Ampere A1: суммарно 2 OCPU / 12 GB, 200 GB block storage, 10 TB/month outbound | По актуальному документу, не старым статьям 4/24. Capacity может отсутствовать; idle VM может быть отозвана. Нужны ARM64 build/native SQLite/runtime checks |
| Google Cloud Free Trial | $300 credit на 90 дней для eligible new users; обычная Linux VM x64 | Пока аккаунт именно Free Trial, начислений нет; при исчерпании кредита/срока ресурсы останавливаются. Ручной Paid upgrade включает оплату. Квоты и eligibility проверяются в аккаунте |
| Azure Free Trial | $200 credit на 30 дней для eligible new users; обычная Azure VM | Без upgrade подписка отключается по кредиту/сроку. Это не доказательство, что trial автоматически покрывает отдельный PlayFab MPS billing |

Источники: [OCI ресурсы](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm), [OCI free account](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier.htm), [Google Cloud Free Trial](https://docs.cloud.google.com/free/docs/free-cloud-features), [Azure Free Account](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/avoid-charges-free-account).

OCI явно указывает отсутствие списаний без перехода на paid account. Для регистрации обычно нужны телефон и карта; возможна временная проверочная блокировка средств, не плата за compute. Это отличается от проверенного MPS activation flow с оплатой overage. Доступность регистрации/карты/региона нужно проверить для владельца. [Регистрация OCI](https://docs.oracle.com/en-us/iaas/Content/GSG/Tasks/signingup_topic-Sign_Up_for_Free_Oracle_Cloud_Promotion.htm).

Владелец выбрал OCI Always Free. Google/Azure trial остаются несогласованными альтернативами на случай недоступной регистрации/capacity. Ни один вариант пока не создан/проверен в аккаунте владельца. Пробный режим одного провайдера не покрывает расходы других сервисов PlayFab/Azure.

### Выбранный OCI стенд — порядок запуска

1. Открыта официальная Oracle Cloud Free Tier Signup. Форма требует страну выставления счетов, имя/фамилию, email; регистрация и принятие условий переданы владельцу. Учётная запись пока не подтверждена. Не создавать второй free account, если он уже есть; использовать действительные данные. Home region выбирается при регистрации, доступность A1 заранее не гарантирована.
2. Проверить аккаунт без Pay As You Go/paid upgrade и остатки квот. Целевая конфигурация: одна Always Free eligible `VM.Standard.A1.Flex`, Ubuntu ARM64, до 2 OCPU / 12 GB суммарно на аккаунт, boot volume 50 GB в home region. Финальные параметры и оценка стоимости проверяются в console; неизвестные/платные опции не включаются. Никакие VM, ключи или firewall правила ещё не созданы.
3. Подготовить ARM64 артефакт и проверить SQLite/процесс уже на Linux ARM64 в закрытом окружении. Readiness packages остаются LocalOnly и не публикуются как игровой endpoint. Отдельно реализовать standalone host, PlayFab identity/admission и trusted TLS; разрешить лишь необходимые сетевые порты, SSH — по ключу с ограничением источника, секреты — вне репозитория и build.
4. Выполнить remote Human smoke двух клиентов, затем нагрузки 10/20 матчей. 2 OCPU не являются обещанием такой вместимости. Зафиксировать восстановление при перезапуске и внешний backup без платных сервисов; idle reclamation Oracle учитывать как риск стенда, не обходить искусственной нагрузкой.

Проверка Windows ARM64 cross-publish и регистрация Oracle — разные результаты. До фактического создания VM и runtime запуска нельзя заявлять, что сервер уже работает в Oracle.

### Вместимость и необходимая реализация

Текущий LocalQueueSettings ограничивает максимум четырьмя активными матчами; специальные QA launchers допускают восемь WSS соединений. Это явные защитные ограничения локального harness. Для цели 20 матчей нужен отдельный production host/profile с соответствующими admission/session/buffer лимитами; простая правка IP или одного MaxActiveMatches не закрывает задачу.

Одна VM может обслуживать много логических матчей; один матч не требует отдельной VM. Стартовая инженерная гипотеза для x64 стенда — 2–4 vCPU / 4–8 GB, с измерением 10 и 20 матчей на выбранной VM. Это не подтверждённая ёмкость: CPU, память, egress и задержки нашей текущей симуляции под такой нагрузкой ещё не измерены.

Обязательные шаги для любого варианта: standalone production host → Linux runtime → identity/admission → доверенный TLS/DNS/firewall → приватное хранение секретов и state → process supervision/backups → remote Human smoke → измерение 10/20 матчей. Для временного обычного VM-хостинга MPS allocation заменяется отдельным hosting adapter; ядро боя и направление PlayFab identity/meta сохраняются, если владелец выберет этот вариант. Полный MPS lifecycle позднее всё равно требует отдельной приёмки.

Платный ориентир, если trial недоступен: небольшой VPS. Для сравнения, [опубликованные цены Hetzner](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/) указывают CX23 €5.49 и CX33 €8.49/месяц без НДС/IPv4; [CX33](https://www.hetzner.com/cloud/cost-optimized/) имеет 4 vCPU / 8 GB. Это ориентир, не готовый заказ: availability, регистрация, IPv4, налоги и дополнительные услуги проверяются отдельно. Такой тариф не следует выдавать за общий hard spending cap — трафик/диски/другие ресурсы могут тарифицироваться отдельно. Если нужен строго фиксированный расход, требуется тариф с предоплатой и подтверждённым отказом от overage, а не только alert.
