# Fusion: возврат после полного закрытия PC-клиента
Status: completed-match PC-to-Editor handoff PASS; native126 PASS; conflicting pending across devices and transport timeout remain open
Last reviewed: 2026-09-15

## Приёмка 15.09: PC → Editor после завершённого матча

Исправление проверено на настоящем Fusion-сервере: `game-bf0cbdc0f4584f7cabac982058eaf435`, instance `4f39054f48194dc7a0b75cb0f7a7b7d6`. Два PC-клиента завершили Human-матч 4:1 (round5, exit0). У tester0 подтверждено18 команд, Pending отсутствовал; соответствующего файла в отдельном Editor storage root до входа не было. Без остановки сервера Editor получил то же назначение/сторону, записал Sequence18, показал MatchResult того же матча 4:1 и штатной кнопкой вернулся в MainMenu. Серверный счёт и команды клиент заново не вычислял; файлы PC-журнала не копировались и не удалялись.

Evidence: `Logs/FusionHandoff/before-editor.json`, `after-editor-journal.json`, `editor-recovered-frame.json`, `menu.txt`, `editor-audit.json`. В Editor нет sequence divergence, startup/UI/encryption errors. Был один автоматический reconnect с успешным `FUSION_RECONNECT_READY serial=2` после серверного `FUSION_QA_REPLY_FAILED TaskCanceledException`; исправление причины самого тайм-аута не входит в эту приёмку. Gateway/client сборки18:04:26/18:04:51, Succeeded0errors. Editor после проверки в Edit, сервер оставлен Ready.

Этот реальный прогон подтверждает переход к завершённому матчу с новым пустым журналом. Он не подтверждает одновременное управление с двух устройств или перенос нерешённой pending-команды между разными журналами. Pending-gap по-прежнему требует явного разбора и не стирает намерение. Native126 PASS включает focused45, без суммирования.

## Контракт

Продолжение F4 из Fusion_PC_Recovery.md. Полное закрытие процесса не отменяет серверный матч: сервер продолжает таймеры, авто-выборы и бой. После нового входа клиент сначала запрашивает authenticated queue Status. Только серверное назначение Matched позволяет открыть матч и его журнал. Нет восстановления владения слотом по локальному файлу и нет нового матча вместо назначенного.

FusionResumeStorage вычисляет SHA-256 ключ из versioned binding: account + server instance + content version + match + side. Идентификаторы не используются как фрагменты пути. Постоянный intent journal хранится отдельно от диагностических логов; смена папки логов больше не теряет command stream/sequence/точный pending payload. Обычный путь — Application.persistentDataPath/FusionResume; закрытый PC launcher задаёт изолированный TD_FUSION_STATE_DIRECTORY. Секретов, bearer-токенов, начислений или доказательств владения в журнале нет.

IMatchIntentLocation подключает путь к существующему ServerClientTransport. После Welcome сохраняются проверки account assignment, match/content, StreamId и NextSequence. Pending-команда пересылается с прежними OperationId, Sequence и payload; server command gate возвращает сохранённую квитанцию. Новый идентификатор или повторное применение эффекта ради восстановления не допускается. Смена аккаунта в приватном auth-файле при reconnect уже запущенного runner отклоняется.

Предыдущие случайные каталоги диагностики не мигрируются. Перед проверкой новая сборка запущена при остановленном сервере, без активных старых матчей. Общие незавершённые изменения репозитория сохранены.

## Актуализация 2026-09-15: sequence при новом хранилище

PC-билд и Unity Editor используют разные storage roots (`TD_FUSION_STATE_DIRECTORY` и обычный persistent path), поэтому новый журнал может присоединяться к уже использованному серверному command stream. Авторитетный Welcome до привязки журнала проверяется транспортом по session, generation, match, side и stream. Если точной pending-команды нет, новый или отставший журнал принимает только монотонный server `NextSequence`, атомарно сохраняет `NextSequence - 1` и лишь затем обновляет память. Rewind отклоняется.

Смена stream и недопустимый разрыв sequence при pending остаются fail-closed: точный pending envelope не переписывается и не удаляется до receipt reconciliation. Магазинный progression journal этим изменением не затронут.

Исторические PC cold-restart приёмки ниже сохраняют факты своих запусков. Они выполнялись при прежнем строгом сравнении `NextSequence` для журнала без pending и поэтому сами по себе не доказывают присоединение нового storage root к уже продвинутому потоку. Новый реальный PC → Editor прогон с завершённым матчем описан отдельно выше.

## Проверка

ServerClient96/96 (исторический прогон): account/instance/content/match/side isolation, небезопасные идентификаторы не выходят из корня, новый context без серверного assignment не создаёт credentials, новый процесс/папка логов находят прежний intent, подтверждение удаляет pending, чужой instance отклоняется. Дополнительный regression: saved intent должен подтверждаться после Hello даже при полностью остановленном производителе новых snapshots. Существующие transport/protocol/runner/mailbox тесты сохранены. Evidence: Logs/FusionMigration/Checks/fusion-cold.trx.

Актуальный полный native ServerClient прогон: **126 PASS**, evidence: `Logs/FusionMigration/Checks/handoff-client-full.trx`. Focused 45 PASS входит в этот же состав тестов и отдельным итогом не суммируется.

Unity MCP compile и Windows builds PASS errors0: финальные dedicated19:43:12, client19:43:35. Первый accepted test ниже выполнен на первоначальных19:32:39/19:33:02; после него добавлена ранняя сверка pending при восстановлении. Иерархии/префабы проходили существующие Unity authoring/build helpers; новый runtime prefab не нужен.

Live test Tools/Fusion/test-gameplay-pair.ps1 -ColdRestartSeconds40 -RestartBoth -RestartPending: **PASS**. Evidence: Logs/FusionServer/game-e0c1c3bfc1ad49d0887faa10051cc202. Два реальных PC-процесса закрыты после server ACK Accepted, до его применения к локальному журналу. Оба Sequence0/Pending.Sequence1. Через40s запущены новые процессы, сервер instance f21fa83f94454bafbc5920657fbb9c4e сохранён. Возврат в тот же Human match/side; получен Draft round3 со счётом0:2, общий финал0:4. Для обоих восстановлена точная pending operation/sequence и прежний Accepted, в локальном журнале Pending=null. 38 shared battle ticks /0 entity hash mismatch; exit0. Это не просто пересоздание runner внутри живого приложения.

После restart counters клиента начинаются заново: ConnectionsMax1 не означает отсутствия прерывания. Доказательство — OldPid/NewPid, player-before-restart.log, server ACK и journals before/after в cold-restart.json. Диагностические JSONL могут потерять буфер при Kill; рабочий intent сохраняется атомарно с Flush(true). Native gap после возврата mean~68–72ms, max136–185ms; данные не являются новым load/visual acceptance.

QA hook TD_FUSION_QA_COLD_PENDING действует только в Fusion QA adapter с явным launcher flag: задерживает доставку уже полученного ACK приложению и оставляет безопасный marker. Никаких публичных fault API, изменений биллинга или live-экономики. После restart launcher снимает flag. Дополнительный режим -RestartAfterResult держит один процесс закрытым до MatchResult второго и проверяет возврат сразу на итоговый экран.

Контрольный both-client repeat на финальной сборке — **PASS**, Logs/FusionServer/game-291e908c550f4c67a87c1d0ed2ffa2ee. Оба процесса закрыты40s с Accepted ещё в pending journal, PID28004→9324 и32600→11888. У обоих первый снимок после старта Draft round3/0:2, старые OperationId/Sequence1 подтверждены прежними Accepted, финальный Pending=null. Общий Human итог1:4, exit0,81 shared battle ticks /0 entity hash mismatch. По одной connection в каждом новом процессе; client0 отбросил24 старых native serial, без отката принятой revision.

Этот контрольный прогон **не принимается как доказательство полной плавности**: native mean100–107ms/max516–986ms; max hold0.68/1.22s, Unity max frame34/39ms. Private source gap~84–86ms и max poll34–36ms не доказывают причину отдельных задержек клиентского render timeline. Причину доставки/SDK scheduling/маршрута следует продолжить измерять отдельным performance этапом, без подмены проверки восстановления настройкой больших буферов.

Финал: manager Stopped, instanceId=null, игровых и server Player процессов нет. Панель http://127.0.0.1:18878/ оставлена доступной. Коммит/push не выполнялись. Source96/96, финальные Windows builds PASS; source/docs diff check PASS. Android и новые provider/экономические настройки не запускались.

## Границы

Первый late-result test game-e01d87a1c9ce427aa7e5783b0e3025bb **не принят**: восстановленный клиент получил MatchResult2:4, но завершился с Pending.Sequence1, без локально обработанного ACK. Итоговый snapshot имел CatchingUp=false. Исходный socket loop проверял pending только после Poll, а финальное состояние могло не обновляться; автоматический выход после результата мог завершить приложение до reconciliation. Исправлено: сразу после Welcome/проверки command stream транспорт сверяет сохранённую команду, до чтения и показа первого snapshot. Неподтверждённый intent не удаляется предположительно. Это устраняет зависимость проверки квитанции от нового боевого снимка; точный сетевой тайминг отказа прежнего запуска не доказан.

Повтор позднего возврата на финальной сборке — **PASS**, Logs/FusionServer/game-d13a91a24c4643df85fbc6d0efd5789a. Процесс0 закрыт после server Accepted в первом драфте, соперник/сервер закончили7 раундов со счётом4:3. Новый процесс30676→32832 вошёл в прежний матч; первая запись после restart — MatchResult, Pending=false, счёт4:3. Exact OperationId bcc58e370e3b42e38361ac16e3332762 /Sequence1 получил ту же квитанцию Accepted единожды в локальном receipt log; JournalAfter.Sequence1, Pending=null. Обе стороны exit0, один MatchId и разные назначенные sides. В этом тесте нет совместного боевого участка после возврата: сравнивать entity hashes или latency как полноценный бой нельзя. Прежний неподтверждённый запуск не включается в PASS.

Это восстановление клиента при живом dedicated authority. Серверный crash/restart, долговечный результат/награда PlayFab и перенос матча между instance не покрыты. Текущий RemoteHost хранит завершённый матч120s после окончания (CompletedRetentionSeconds), затем удаляет: поздний возврат за пределами окна не гарантирует итоговый экран. Перед релизом нужен отдельный durable settlement/result contract; это не изменено скрыто этим патчем.

Auth expiry/renewal, двойной вход аккаунта, контролируемая command/network fault matrix и нагрузка10–20 матчей остаются следующими этапами. Cold restart до получения assignment/в момент отправки Join не сохраняет uncertain Join operation на диск — текущий шаг покрывает уже назначенный матч. Очистка старых intent-файлов и UX просроченного результата относятся к следующей работе с lifecycle/хранилищем. Android отложен владельцем; gameplay encryption остаётся согласованным исключением только закрытой QA.
