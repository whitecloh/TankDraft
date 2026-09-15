# Проверка сетевых сбоев
Status: encrypted TCP faults verified; Android USB lifecycle verified separately; radio and IP packet QA pending
Last reviewed: 2026-09-13

## Объём

Android ARM64 на физическом Xiaomi проверен отдельно: [Android_Local_QA.md](Android_Local_QA.md). USB tunnel не проверяет Wi-Fi/LTE.

Следующий локальный срез S4 проверяет настоящий WSS между двумя Windows Unity Player и standalone сервером через `LocalFaultProxy`. Прокси принимает только loopback `127.0.0.1:18783`, передаёт поток только на `127.0.0.1:18784` и не завершает TLS. Login по HTTPS и WSS проходят ту же прослойку; private key и bearer остаются вне неё. Existing TLS pin проверяется клиентом как прежде.

Настройки стенда находятся в `Backend/Config/local-faults.json`; это QA-конфиг, не баланс и не production network settings. Режим включается явно:

```powershell
./Tools/Backend/run-server-client.ps1 -NetworkFaults
./Tools/Backend/verify-network-fault-run.ps1 -RunDirectory 'Logs/BackendClient/tls-fault-<run id>'
```

Player используется из существующей MCP-сборки ServerMatch. Авторитет, драфт, UI, ECS и authored prefabs не заменяются тестовыми реализациями. Облачные вызовы, изменение системного firewall, установка драйверов, раскрытие внешнего listener и изменение trust store не нужны.

## Сценарии

| Воздействие | Механизм | Критерий |
|---|---|---|
| Задержка и джиттер | Для каждого направления: 40 ms плюс 0–80 ms на chunk, фиксированный seed | Реальный ненулевой переданный объём, разные применённые задержки, завершение матча |
| Разрыв соединения | На 12-й секунде закрываются активные пары TCP | Есть реально закрытые соединения, новые подключения и восстановление текущего состояния |
| Длительная пауза | На 25–33-й секунде передача удерживается дольше клиентского timeout | Есть удержанные chunks, после восстановления оба клиента продолжают матч |
| Потерянный ACK и process kill | Existing QA hook side 0 после отправки команды, access отзывается | Тот же journal/StreamId, pending подтверждён без повторной мутации |
| Отсутствие игрока | Existing side 1 offline 40 секунд, access истекает | Сервер продолжает матч; после входа более поздний раунд и новое поколение доступа |

Seed задаёт последовательность jitter; точное разбиение TCP потока на chunks и планирование ОС недетерминированы. Повторяемы настройки и события, а не каждый пакет или кадр. Это stream-level задержки/пауза/разрыв; стенд не измеряет процент потерянных IP-пакетов и не имитирует TCP retransmission/reordering. Прокси не переставляет и не удаляет отдельные байты живого TLS-потока.

## Ограничение ресурсов и evidence

Прокси хранит ограниченное число соединений и по одному буферу на направление. Обратное давление обеспечивается последовательным чтением/записью; бесконечных очередей нет. При остановке отменяются операции, закрываются пары TCP и ожидаются собственные задачи. Отчёт `network-faults.json` содержит счётчики и длительности, без payload, headers или токенов.

Verifier сначала выполняет существующую TLS-проверку финала: одинаковый authoritative state, score, полный entity hash на общих тиках, отсутствие pending intent, корректные generation/stream и exit codes. Затем проверяет, что сетевые воздействия действительно произошли и передали данные, а не только были включены в конфиге. Результаты сохраняются в ignored `Logs/BackendClient`.

## Проверено 2026-09-13

Финальный игровой прогон: `Logs/BackendClient/tls-fault-a9c9ef56ba604e8aae3b1f0234940e8a`. `verify-network-fault-run.ps1` — PASS:

- Семь раундов, 3:4, revision 2692; оба клиента получили одинаковый authoritative финал, pending intent отсутствуют.
- 23 общих battle ticks: ноль несовпадений полного BattleEntityState. Это сравнение наблюдённых общих тиков, не доказательство одинакового кадра при разной задержке.
- Reset действительно закрыл четыре соединения; stall затронул два соединения и три chunks. Зафиксированы выбранные задержки 40–120 ms, 2137 delayed chunks.
- Оба клиента переподключались и обновляли доступ: финальные generation 8 и 6, StreamId каждой стороны неизменен. Side 0 восстановил pending после process kill; side 1 вернулся в более поздний раунд после 40 секунд offline.
- 22 принятых подключения за запуск; одновременно максимум четыре, отказов по лимиту нет. `failures=0`, `activeAtStop=0`; порты 18783/18784 освобождены после остановки.
- Время стенда 173195 ms; TLS wire bytes: client→server 98380, server→client 7150582. Суммарный средний трафик двух клиентов, включая login/handshake/reconnect: 40.87 KiB/s. Это не payload bandwidth одного игрока, не RTT и не оценка ёмкости 100 CCU.
- Коды завершения Player `[0,0]`, launcher 0; оба итоговых screenshot просмотрены. Существующая Unity-сборка из TLS-этапа использована без изменения клиента/UI/боевых данных. Editor остаётся в чистом MainMenu, Edit Mode.

После прогона уточнён только учёт ожидаемой отмены connect-timeout внутри прокси; повторно собраны финальные исходники, выполнен TLS/session verifier (baseline и extended PASS) и raw byte-order/shutdown probe `Logs/BackendClient/fault-proxy-byteorder-e6e8dc7c5850405496fb9e4e8dde8f3d`. В обе стороны передано 49157 / 32771 байт с точным SHA-256 совпадением порядка; `activeAtStop=0`, `failures=0`. Raw probe проверяет relay/order/cleanup, не выдаётся за отдельный полный fault-матч. Отчёты предыдущих диагностических попыток остаются в ignored Logs.

Также исправлен launcher: отмена/общий лимит automated-прогона без завершившихся Player теперь считается ошибкой, а не успешным запуском.

## Остаток S4

Реальный Android/iOS background, force-stop/relaunch и Wi-Fi/mobile handover; IP loss/reordering и ограничение пропускной способности системным эмулятором; предельный slow consumer; профилирование 20/50/100 CCU. На старте этого среза `adb devices` не показывает подключённых устройств. Локальный Windows тест не подтверждает мобильный lifecycle и не заменяет MPS/production identity QA.
