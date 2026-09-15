# R3.2 — постоянный результат и ограниченная тестовая награда
Status: R3.2 PC QA with audited operator resolution implemented; 177 backend tests PASS; live compensation not exercised
Last reviewed: 2026-09-15

## Контракт текущего шага

15.09 уточнение владельца: разумная сложность, сохранность покупок/прогресса, допустима компенсация в пользу игрока. Вместо автоматического определения неизвестного Legacy исхода реализована локальная процедура ConfirmApplied по доказательству либо CompensateOnce с допустимой переплатой. Это явное уточнение прежнего критерия «никогда второй награды», не обещание exactly-once Legacy. Исследование и границы покупок — [Economy_Reliability.md](Economy_Reliability.md).

Владелец разрешил следующий шаг после Legacy R3.1. Fusion dedicated и PlayFab профиль/Legacy Inventory сохранены; Azure не подключаем. Новый журнал завершённых матчей — локальная SQLite база доверенного backend, отдельная от PlayFab профиля и инвентаря. Она хранит результат и обязанность выдать награду, а не второй кошелёк. Баланс по-прежнему читается из PlayFab.

`Backend/TankDraft.Server.Settlement` содержит `SqliteSettlementStore` и `SettlementDispatcher`. Абсолютный путь постоянен между запусками: приватный `fusion-settlement/results.sqlite`, вне Logs/builds и репозитория. SQLite WAL/FULL, исключительная блокировка одного writer, лимит 10000 результатов и 64MiB страниц БД. Sidecar/WAL и блокировку нельзя удалять для обхода ошибки. При reopen проверяются политика, результаты, участники, суммы, состояния и полнота обязанностей; повреждённая/пустая существующая база отвергается. Политика фиксируется в БД; её изменение требует отдельной миграции. Размер WAL/резервных копий не входит в лимит страниц основной БД.

RemoteMatchService получает завершение только из авторитетного ServerMatchRuntime.ReadCompletion. ResultId = SHA256(MatchId + "|" + ContentVersion). Одной транзакцией сохраняются неизменяемый результат и награды обоих людей; бот кошелька не имеет. Точный дубль ничего не добавляет; другой результат для того же match/result ID отвергается. COMMIT выполняется перед публикацией финального Snapshot/ACK, перед retention/Leave. При ошибке записи финал не публикуется и authority прекращает обслуживание. Незавершённая симуляция при потере процесса по-прежнему не восстанавливается этим патчем; это отдельный R2 контракт.

Результаты сохраняются после retention120s и нового InstanceId. Авторизованный ProfileGet возвращает RecentResults только своего аккаунта: счёт, сторона, тип соперника и состояние награды; идентификаторы аккаунтов и provider payload наружу не выдаются. Вёрстка истории/индикатора ожидания относится к R3.3. Существующий клиент читает обновлённые балансы при загрузке меню, не вычисляет их из результата матча.

## Выдача и неопределённый ответ

Состояния: Pending → Sending → Applied; любой неопределённый provider вызов → NeedsReview. Sending записывается до HTTP. После перезапуска оставшийся Sending переходит в NeedsReview без нового начисления. У одного аккаунта с NeedsReview последующие награды остаются Pending; другие аккаунты продолжают обслуживаться. Заблокированные записи исключаются до ограничения размера batch, чтобы не мешать другим игрокам. Обработчик single-flight; HTTP выполняется вне блокировки игрового цикла, раз в секунду проверяется локальная очередь.

Мягкий drain ждёт завершения активных матчей, доступных Pending и уже работающего обработчика; отдельный тест с заблокированным provider подтверждает это. Force stop/потеря процесса могут оставить Sending: следующий запуск консервативно переводит его в NeedsReview. Сверка неопределённых выдач не запускается автоматически. Текущий `--inspect-qa-settlement` делает только provider reads и открывает локальный журнал под исключительной блокировкой (Sending-recovery может изменить состояние самого журнала), поэтому запускать его при остановленном manager authority.

PlayFabLegacyRewardProvider проверяет серверный allowlist, сумму/код/ResultId и прочитанный баланс/overflow, затем делает ровно один Server/AddUserVirtualCurrency с VirtualCurrency. Только preflight GetUserInventory повторяется до трёх попыток с паузами 250/750 мс; при исчерпании сохраняется NeedsReview. Ответ Add проверяется на аккаунт, валюту, BalanceChange и диапазон баланса; сравнение с устаревшим глобальным балансом удалено. CustomTags.tankdraftResultId — диагностическая метка, **не ключ идемпотентности**. Операций выдачи и разрешения NeedsReview в клиентском протоколе нет.

Это гарантирует отсутствие слепой повторной отправки нашим обработчиком, но **не полностью автоматическое exactly-once выполнение Legacy вызова при неизвестном исходе**. NeedsReview сохраняет результат и право на награду; нельзя автоматически объявить такую награду выданной или потерянной по одному чтению баланса. Локальная процедура ниже закрывает разбор для текущего QA: доказательство выдачи либо разрешённая однократная компенсационная попытка. Автоматическая сверка всех операций не входит в этот простой срез. Ручное изменение SQLite, удаление журнала и прямой повтор Add остаются запрещённым recovery.

## Разбор оператором

Локальная приёмка 15.09: Settlement15 + Meta58 + RemoteHost75 + ServerManager7 + Match22 = 177 PASS на native.NET10. Результаты Logs/Settlement/*-reliability.trx. MetaLiveProbe собирается без предупреждений. Новые проверки: bounded read retry, lost write без retry, concurrent balance, profile load/save failure без reset, повтор сохранённой операции, audit/compensation/reopen/corrupt-state. Потерянные ответы моделировались тестовыми провайдерами; реальную компенсацию в PlayFab не выполняли. Старый live 2PC/restart evidence ниже относится к обычной награде, не к новой процедуре.

Обновлённый ServerManager опубликован локально в Builds/Fusion/Manager; панель перезапущена и проверена: Stopped/instanceId=null. CLI неверный action/QA index отвергает с exit2 до открытия журнала. В этой проверке существующий приватный журнал и аккаунты не изменялись; Unity клиент не пересобирался.

Остановить игровой сервер в панели. Запускать из корня проекта; утилита использует только существующий приватный QA config и индексы двух allowlisted аккаунтов, не читает серверный ключ и не делает HTTP. Исключительная блокировка запрещает открыть журнал рядом с работающей authority. Открытие журнала выполняет recovery Sending и добавляет совместимую audit-таблицу schema1, поэтому list/preview не являются физически read-only для SQLite.

```powershell
& .\Logs\BackendSdk\dotnet.exe run --project Backend\TankDraft.MetaLiveProbe -- --review-qa-settlement list
```

Для выбранного ResultId сначала посмотреть preview (заменить `<result-id>` и `<evidence-ref>` реальными значениями):

```powershell
& .\Logs\BackendSdk\dotnet.exe run --project Backend\TankDraft.MetaLiveProbe -- --review-qa-settlement compensate-once 0 <result-id> <evidence-ref>
```

После проверки добавить `--apply`, чтобы записать решение. `confirm-applied` вместо `compensate-once` разрешён только при независимом подтверждении account/result/amount; evidence-ref — идентификатор локального акта/тикета, не секрет или произвольный текст. Утилита не проверяет достоверность акта вместо оператора. Одна лишь текущая сумма в кошельке доказательством не является.

ResolveReview атомарно сохраняет audit и меняет состояние. ConfirmApplied → Applied/verified_external без HTTP. CompensateOnce → Pending/compensation_authorized; выдача произойдёт обычным обработчиком при следующем старте authority. Точный повтор решения ничего не меняет; другая повторная компенсация запрещена даже после restart. Если компенсация тоже неопределённа, остаётся NeedsReview с прежним audit; дальнейшее подтверждение возможно по факту, третьего автоматического начисления нет. Успешная компенсация сохраняет специальную причину, её нельзя выдавать за доказательство первого вызова.

Не более двух попыток Add на обязанность с разрешённой компенсацией: обычная и дополнительная. Максимальные тестовые поступления — до20CO на аккаунт вместо обычных10CO. Никаких произвольных сумм/аккаунтов/покупок в этом инструменте нет.

## Закрытый QA

Backend/Content/settlement-qa-review.json фиксирует техническую политику qa-r32-v1: CO=1 за победу или поражение, максимум10 обязанностей на аккаунт. Это не GD баланс/магазин. После лимита результат сохраняется, награда Skipped/qa_reward_cap. Суммы больше1, иной код, версия или лимит отвергаются live bootstrap. Два существующих allowlisted QA аккаунта, никакой выдачи боту и никаких новых игроков.

Private ManagerOptions.SettlementSettingsPath указывает на playfab-settlement-qa.json с Mode=LegacyTestRewards, DatabasePath и Policy. Включение требует уже проверенного PlayFabLegacyClosedQa; без отдельного пути награды отключены. Настройки/ключи не передаются в Unity gateway/client. Тариф, карта, реальные покупки и API policy не меняются этим шагом. Legacy клиентские write-deny из R3.1 сохраняются. Private /readyz честно сообщает EconomyWritesEnabled; старый Unity capability label plaintext-qa-no-economy описывает прежний transport QA, не используется как разрешение экономики — authoritative gate находится в backend.

## Проверки

- Native .NET10: Settlement9/9, Meta44/44, RemoteHost75/75, Manager7/7, Match22/22 — всего157; TRX в Logs/Settlement. Inspector build0errors/0warnings. Изменение soft drain дополнительно проверено после первого live матча, manager перепубликован и повторно запущен.
- SQLite reopen/дубли/conflict/writer lock/cap/corrupt policy, отсутствие чтения чужого результата, starvation при17 заблокированных аккаунтах, single-flight, cancellation, provider применил изменение и потерял ответ, частичное выполнение двух наград.
- Авторитетный offline Human/Bot матч до результата, commit-before-publication, retention, новый instance/свой history, ошибка журнала закрывает публикацию; fake provider не выдаёт повтор после reopen.
- Реальный baseline двух QA: Results0, Applied0, Coins0 (Logs/Settlement/live-before.json).
- Live два PC клиента: game-1524e4c286f24fbaa44deb41ac7efc92, instance08b9b00335bb42ce99cdfc66789194b8, общий Human MatchResult2:4 PASS. Затем настоящий PlayFab у обоих: Coins1, result1, Applied1, Pending0/NeedsReview0 (Logs/Settlement/live-after-match.json).
- Полный restart панели и новый authority instanceb8e92a78a1334892bbeaa57c9f256dad с тем же журналом; повторный обработчик не начислил ещё раз. Реальные balances/receipts побайтно сравнивались как нормализованный JSON: unchanged PASS (Logs/Settlement/live-after-restart.json). Этот restart проверял ядро без Photon gateway, новый бой не создавался. История с авторизацией/новым instance проверена интеграционными тестами; отдельный экран истории пока не реализован.
- Финал: manager Stopped/instanceId=null, панель18878 доступна, игровые клиенты завершены. Новые Unity builds/assets не требовались; использованы прежние PC builds, backend/manager обновлены. Android/fault/load/new-encryption не проверялись. Тариф, карта, реальные покупки, provider policy и существующие профили не менялись; два тестовых баланса увеличены с0 до1 согласно QA политике.
- MCP начальная read-only проверка Editor прошла; финальная editor-application-get-state через CLI завершилась timeout/retries. Финальное состояние Editor этим вызовом не подтверждено. Это не отменяет завершённый standalone Human test и .NET проверки; Unity сцены/ассеты в этом патче не менялись.

Документация: [Legacy AddUserVirtualCurrency](https://learn.microsoft.com/en-us/rest/api/playfab/server/player-item-management/add-user-virtual-currency?view=playfab-rest) описывает Amount/PlayFabId/VirtualCurrency и не предоставляет IdempotencyId; [V2 retries](https://learn.microsoft.com/en-us/gaming/playfab/economy-monetization/economy-v2/tutorials/idempotent-transactions-and-retries) описывает отдельную гарантию V2, которую Legacy не наследует.
