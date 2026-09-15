# Проверка
Status: active
Last reviewed: 2026-09-12

Проверка соответствует изменению. Docs/config: parse, ссылки, diff --check. Пакеты: resolve и compilation/Console. Код: компиляция и точечные поведенческие тесты. UI: структурная проверка prefab плюс screenshot в целевом viewport. Сеть: два клиента/сервер, таймаут/reconnect/повтор команд; бот не доказывает PvP.
Нельзя называть набор тестов зелёным, если Test Runner не закончил работу. Existing failures отделять по сохранённому baseline; отсутствие baseline не равно отсутствию ошибок.
Критичные случаи: четвёртая победа; камбэк правильной стороне; дубли команд/наград; reset RoundState; сохранение MatchArmy; сброс match buffs; version mismatch; spawn/pool reuse; гейты/недостаток средств.
Для плотного боя проверить целевое устройство. ECS/пулинг сами по себе не доказывают FPS.

