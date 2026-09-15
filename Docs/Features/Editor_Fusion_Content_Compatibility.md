# Запуск сетевого матча из Editor: совместимость сборок

Дата: 2026-09-15. Статус: исправлено; обычный Editor-вход и завершение серверного матча с ботом проверены.

После расширения боевого контента/схемы Editor использовал новые исходники, тогда как серверное ядро и Unity-шлюз были собраны около17:00. Кроме того, `ServerClientSettings` и `NetworkQueueSettings` сохраняли старый ContentVersion `268f3d840dd10bc0b7ddeab3c19c9e61f5f3950a4987553797c688d1d9e8750c`, тогда как battle/meta export уже имел `0de1f0fed7d897de3fc75c4be524dcc791c06a73e0c6cbf03c5c539d6b4c0937`.

Наблюдение: Fusion подключался, Join завершался RequestFailed, счётчики серверной очереди/матчей оставались0. При исправлении только ServerClientSettings возникал явный `Queue content mismatch` в Start очереди, затем нажатие кнопки приводило к null queue. Подключение к Photon само по себе не подтверждает совместимость игрового клиента и authority.

`FusionContentCompatibility.Sync` проверяет SHA256 экспортированного JSON и синхронизирует **оба** authored SO через SerializedObject. Метод вызывается в ExportContent после согласованного battle/meta экспорта и перед штатной сборкой пары gateway/client. Повторный вызов идемпотентен. `FusionMatchLauncher` не считает меню готовым и не запускает Join, пока queue не инициализирована; ранний клик возвращает понятную причину вместо NullReferenceException.

Обновлены сборки Manager/authority, Fusion Server и Fusion Client. Перед пересборкой пустой сервер завершался штатным drain; существующие данные аккаунтов, журналы, настройки провайдеров и лимиты не удалялись/не менялись. Сервер нужно держать Ready в панели18878. Для рабочего Editor используется Fusion flow, а не устаревший `run-matchmaking.ps1`.

После изменения несовместимой схемы или контента необходима совместная актуализация сервера и клиента; нельзя считать локальные симуляционные тесты проверкой обычного Editor-входа. Экспорт сам по себе не пересобирает уже работающий сервер.

Проверка 15.09: сборки Fusion Server и Client завершились успешно, без ошибок (22:42); Manager/authority опубликован локально. Повторный Sync возвращает одинаковый хэш. Через FusionEditorPlay и обычный StartBattle_Button выполнен поиск → назначение серверного Bot → ServerMatch → MatchResult. Instance `de236f36f3b74d338dbcbec2224ed115`, match suffix `3a0684160754467d8444c69a0442de3b`; клиентские `_latest` и `_presented` подтвердили MatchResult, Round=4, Revision=1542, `_matchCompleted=true`, `_failure=null`. Manager подтвердил completedMatches=1, activeMatches=0. Очередь: `Logs/FusionEditor/89e184158c8a43b185bd48e38f7d8629/queue.jsonl`; итоговая проверка: `Logs/EditorFusionCompatibility/acceptance.txt`. После результата Editor выведен из Play Mode; сервер оставлен Ready.

Это реальный вход Editor через Fusion к authority на этом ПК, с серверным ботом. Два удалённых игрока, Android, визуальная плавность и новые экономические операции в этой проверке не принимались. Предыдущие 316 native-тестов относятся к defense-патчу и в данном исправлении не перезапускались.
