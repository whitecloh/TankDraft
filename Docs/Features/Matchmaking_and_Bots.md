# Подбор и боты
Status: local queue and reconnect presentation fixes verified on Windows/Android; cloud gate closed
Last reviewed: 2026-09-13

## Что делают другие игры
Blizzard официально вводила AI-соперников для новичков/низкого MMR в Standard (патч 24.4, 2022) и Battlegrounds (28.2, 2023). Это датированные решения, не утверждение о сегодняшней конфигурации каждого режима.
В Mercenaries статья 2021 описывает подбор с учётом рейтинга, уровней/способностей и ролей, а также AI после 1–1.5 минут при определённых порогах рейтинга.
Источники: https://hearthstone.blizzard.com/en-us/news/23852687 ; https://hearthstone.blizzard.com/en-us/news/24008697 ; https://hearthstone.blizzard.com/en-us/news/23730851/developer-insights-ratings-and-matchmaking-in-hearthstone-mercenaries-pvp .
По Draft Showdown найдены пользовательские подозрения о ботах, но официального технического подтверждения состава матчей в данном поиске нет. Не объявлять всех соперников ботами.

## Предложение TankDraft
1. Проверить версию контента/правил, регион и допустимый latency; искать человека близкого skill MMR.
2. Учитывать отдельно силу выбранной колоды/мастерства и доступную арену, с расширяемыми границами; не создавать бесконечно много изолированных очередей по каждому уровню.
3. Постепенно расширять диапазон навыка/силы. По ограничению ожидания создавать подходящего бота.
4. По предложению владельца от 2026-09-13 стартовый playtest: искать человека до 10 секунд, затем fallback-бот. Срок хранится в серверном конфиге. Человек найден раньше — матч начинается сразу. Клиентский таймер только отображает серверный deadline.
5. Race между найденным человеком и ботом решает сервер атомарно: одна заявка → один matchId. Отмена идемпотентна.
6. Бот выбирает реальные офферы по тем же правилам. Skill — качество оценки/синергий и контролируемые ошибки; strength — заданный бюджет колоды. Не усиливать скрыто после выбора игрока.
7. Разные профили: новичок, сбалансированный, контрроль, высокая сложность; библиотека валидных колод с вариациями seed. Не нужен LLM на каждый ход.
8. Доступный обычный бой гарантировать fallback-ботом; глобальный верхний рейтинг не должен фармиться бесконечными победами над AI. Предлагаются отдельные правила очков/лимиты, ещё требуют решения.
9. Disconnect-замена ботом и fallback в очереди — разные состояния; сначала reconnect grace, затем заранее определённая политика.
10. Человек/AI — явный тип соперника в данных и QA, не имитация доказательства live PvP. Подачу AI и влияние на рейтинг согласовать при дизайне.

## Реализованный локальный срез

- MainMenu → IMatchLauncher / NetworkMatchLauncher → TLS queue → назначенный ServerMatch. Используется существующий authored UIMessageWindow; NetworkQueueSettings содержит тексты, polling и ссылку на имя сцены. MainMenuScope хранит ссылку на SO. Боевые сущности остаются authored config → runtime → prepared prefabs/pools.
- Backend LocalMatchmaking атомарно исполняет Join/Status/Cancel/LeaveCompleted. Одна локальная identity имеет одну текущую заявку/назначение. Deadline задаётся local-queue.json (10 секунд), ожидание измеряется монотонными серверными часами и не меняется при переводе UTC. Если человек успел раньше, создаётся human match; на границе истечения сначала создаётся bot match. При отмене уже назначенный матч сохраняется.
- Сервер валидирует contentVersion, четыре различных поддержанных юнита и приказ; клиент не выбирает matchId, side, seed или тип соперника. QA разрешает весь authored roster — это не production проверка владения коллекцией.
- Бот действует на сервере через те же CommandEnvelope, sequence, receipt и domain validation; fixed authored deck/простая политика выбора — стартовый AI без MMR.
- По результату игрок возвращается в меню и может начать новый поиск сразу, независимо от выхода второго участника. Повтор Leave после потерянного ответа идемпотентен в пределах bounded retention; очистка старого матча не удаляет новый тикет.
- Обновление access использует Reauthenticate в текущем WSS, сохраняет cursor и буфер. CatchingUp блокирует решения, но не означает потерю сети и не сбрасывает анимацию. Настоящие сетевые разрывы по-прежнему запускают reconnect.
- Подтверждённый MatchResult завершает транспортный сеанс: результат остаётся на экране без дальнейшего polling и reconnect после очистки серверного матча. Leave для уже очищенного матча — безопасный no-op, не затрагивающий новое назначение.

## Самостоятельный локальный запуск

Из корня проекта в PowerShell:

```powershell
# Два ручных окна ПК; нажать «В бой» в обоих в пределах 10 секунд.
./Tools/Backend/run-matchmaking.ps1 -Mode windows-pair
# Один ПК; после ожидания сервер назначит бота.
./Tools/Backend/run-matchmaking.ps1 -Mode windows-solo
# Телефон + ручной ПК; телефон подключён по USB, USB debugging разрешён.
./Tools/Backend/run-matchmaking.ps1 -Mode android-pair -DeviceSerial bfaddd98
# Только телефон, сервер на ПК; через 10 секунд — серверный бот.
./Tools/Backend/run-matchmaking.ps1 -Mode android-solo -DeviceSerial bfaddd98
```

Для проверки двух последовательных матчей добавить -Automated. Требуются menu-сборки ServerClientBuild.QueueMenu() / ServerClientAndroidBuild.QueueMenu(); legacy Queue() начинает фиксированную QA-сцену. На телефоне пакет com.tankdraft.localqa. Локальный сервер ограничен одним часом ручного теста, автоматический прогон — 850 сек. Остановка launcher завершает сервер и удаляет принадлежащий ему adb reverse/Android bootstrap. Не запускать два host на одном 18783 одновременно.

Automated android-pair ожидает подтверждённый сервером Searching телефона перед запуском Windows, затем проверяет, что назначен именно Human. Получение Bot завершает pair-прогон ошибкой; solo-прогон ожидает Bot. Готовность телефона фиксируется в pair-search-ready.json без токенов. Эта координация относится только к автотесту и не продлевает игровые 10 секунд.

Queue account grant отделён от per-match grant. На ПК он передаётся environment дочерним процессам; на Android хранится в приватном bootstrap на время серверного сеанса, позволяя закрыть/открыть приложение и восстановить серверное назначение. Токены не пишутся в match journals/логи/Git. В assignment.json пишутся только matchId/side/opponentKind. После перезапуска всего локального host очередь и QA identity не восстанавливаются; production identity и распределённое хранилище — следующий самостоятельный этап.

## Проверки и границы

Core: 10 тестов, включая точную границу timeout/cancel, перевод UTC, идемпотентность, неверную колоду и полный серверный матч → leave/retry/new join. --verify-queue-http проверяет pinned TLS, неизвестный bearer, точную схему JSON, human pairing и выдачу назначенного match grant. verify-matchmaking-run.ps1 проверяет два результата, общие server ticks, отсутствие pending и seamless access.

Локальные лимиты: 8 ожидающих, 4 активных матча, bounded completed retention и 8 WSS; это пределы harness, а не результат нагрузочного теста. Фактические прогоны приведены ниже.

Облачные ресурсы не включены. PlayFab/MPS/Azure остаются за zero-charge gate. Публичный matchmaking, рейтинговый подбор/регионы, production login и гарантии восстановления host этим срезом не заявлены.

### Windows runtime PASS

Прогон Logs/BackendClient/queue-5f6552a7bb034bb493338ebfe890630d: 2 human-матча подряд на обоих Player, итоговые состояния совпадают, 531 общий Battle tick без расхождений. Connections=[1,1,1,1], AuthGeneration=[5,5,5,5], 1053 CatchingUp snapshot без disconnected Battle. P95 Time.unscaledDeltaTime в диагностической выборке ~16.74 ms (это выборка при изменении snapshot, не полноценный per-frame profiler). Оба Player ExitCode=0.

Предыдущий прогон queue-5e027b5e96254f7e8426f4fec7e5faa2 совпал с Android IL2CPP-сборкой на том же ПК: оба клиента получили реальный timeout, затем восстановили одинаковый результат. Этот прогон не объявляется seamless PASS. Не запускать тяжёлую сборку на машине локального сервера при оценке плавности. Локальный scheduler логирует операции >250 ms с ограничением частоты.

### Android + Windows runtime PASS

Прогон Logs/BackendClient/queue-android-c0cce84aa1ba90a3b589225564d3cf41: два human-матча через главное меню на Xiaomi ARM64 и Windows. 518 общих Battle ticks без расхождений, Connections=[1,1,1,1], AuthGeneration=[5,5,5,5]. Full results и return-to-menu/requeue проверены на обоих клиентах. P95 Time.unscaledDeltaTime объединённой диагностической выборки ~22.24 ms. Игровых исключений Unity/AndroidRuntime не обнаружено; vendor debug сообщения об affinity/heap introspection не интерпретируются как ошибка матчевого кода.

### Android queue recovery + Bot runtime PASS

Прогон Logs/BackendClient/queue-android-83973d951a631438209ad503eec30219: приложение force-stop/reopen во время Searching, прежний TicketId сохранён (search-restart.json), затем два полных серверных Bot-матча с возвратом в меню. Connections=[1,1], AuthGeneration=[3,3]; 291 CatchingUp snapshot без ложного disconnect. Команда воспроизведения: run-matchmaking.ps1 -Mode android-solo -DeviceSerial bfaddd98 -Automated -RestartDuringSearch.

В боевом срезе реализован приказ order.reinforce_armor (или пустой слот); остальные карточки приказов меты пока не подключены к серверному бою. Для ручной сетевой проверки используйте начальную колоду и этот приказ. MMR, разные бот-профили и live ownership остаются вне этого локального среза.

### Проверка терминального состояния на последних сборках

Прогон Logs/BackendClient/queue-android-749c5527268747192a616be27eee111a: по два полных Bot-матча на Windows и Android, Connections=[1,1,1,1], AuthGeneration=[3,3,3,3], 591 CatchingUp snapshot без disconnected Battle. Проверены MatchResult с остановленным транспортом и последующий return-to-menu/requeue. Этот запуск планировался как human pair, но разница времени автозапуска превысила 10 секунд: строгий Human verifier отклонил прогон, Bot verifier подтвердил четыре независимых матча. За PvP-доказательство этот прогон не засчитывается.

### Финальный Android + Windows PvP PASS

После координации автозапуска прогон Logs/BackendClient/queue-android-7ec8e8445bc0551ef451c1bb6f5fa6c4 прошёл строгий Human verifier на последних сборках: 2 общих матча, 4 завершённых клиентских сеанса, 502 общих Battle ticks без расхождений, Connections=[1,1,1,1], AuthGeneration=[5,5,5,5]. 963 CatchingUp snapshot без disconnected Battle; P95 диагностической выборки ~22.24 ms. После MatchResult оба клиента вернулись в меню и вошли во второй Human match. В логах не обнаружены игровые Unity/AndroidRuntime exceptions или transport timeout. Backend core 10/10 и queue HTTP verifier прошли; Unity восстановлена в StandaloneWindows64, MainMenu сохранена, compilation failed=false. Windows build: 0 errors/0 warnings; Android IL2CPP ARM64: 0 errors/3 warnings, APK установлен на bfaddd98.
