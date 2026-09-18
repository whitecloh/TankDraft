# Персональный пакет внешнего тестера
Status: personal archive built; extracted startup and full server-bot match verified; external device acceptance pending
Last reviewed: 2026-09-17

## Исправление launcher Fix1

Внешний тестер сообщил `private runtime setup / Logs: not created`. Исходный
catch скрывал исключение, поэтому точная причина на его Windows не установлена.
Убраны Get-Acl/Set-Acl и зависимость от автозагрузки Security module: Windows
PowerShell 5.1 применяет новый SID-only DACL через .NET, не копируя owner/group
и не преобразуя существующие ACL в имена аккаунтов. Путь берётся из Windows
Known Folder вместо LOCALAPPDATA env. Права по-прежнему только user+SYSTEM,
reparse paths отвергаются, небезопасного fallback нет; admin не нужен.

CheckRuntime.cmd проверяет папки/запись без auth, сервера или запуска игры.
При ошибке до логов выводятся step/type/HResult без текста provider response.
StartGame.cmd закреплён за штатным Windows PowerShell, не первым exe из PATH.
`Tools/Fusion/test-tester-runtime.ps1` PASS: Security module недоступен,
повторная папка, Unicode/скобки в пути, защищённые file/directory ACL,
неизвестный SID в старом ACL, пустой LOCALAPPDATA. Проверка распакованного Fix1
также PASS. На Windows внешнего тестера повторная проверка ещё нужна.

Архив `Builds/Distributions/TankDraft-PC-Tester01-20260917-fix1.zip`:
43 659 301 байт, SHA256 `e939dda9c5e9b88c74bb27f8af6860bf4f177424b2e7c35d7e61a7652a549418`.
Малый патч `Builds/Distributions/TankDraft-Launcher-Fix1.zip`: 6 801 байт,
SHA256 `55f1b338a66a3da5ea51d276ca0877821634cdd460c92a070bc31bc03152eb7d`.
Он содержит только launcher/README/checker: распаковать поверх старого пакета.
281 прочий файл исходного ZIP побайтно сохранён; игра, identity, PackageId и
resume-path прежние. Auth/игровой тест не повторялся, чтобы не занять аккаунт
внешнего тестера; локальная проверка исправления полностью offline.

## Решение

По поручению владельца формируется персональный Windows x64 ZIP для одного
внешнего тестера. Используется существующий allowlisted QA tester1; tester0
остаётся владельцу/Editor. Новый PlayFab аккаунт, гранты, покупки и настройки
провайдеров не создаются. Архив не публикуется и не отправляется автоматически.

## Состав и запуск

`Tools/Fusion/make-tester-package.ps1` берёт текущий Windows client и только
разрешённые файлы Unity runtime; рядом кладёт portable `StartGame.cmd`,
`Launch.ps1`, `README_RU.txt`, `ServerCode.txt`, `TesterAccess.json`.
Owner-only StartGame.cmd, произвольные архивы в Client, логи, серверные бинарники,
репозиторий и .codex-secrets не копируются.

В персональном TesterAccess находится только credential существующего tester1
для Client/LoginWithCustomID (CreateAccount=false), ожидаемый account id и
публичные идентификаторы Title/Fusion/content/package. Это доступ к тестовой
учётной записи: один пакет — один тестер, не публичный дистрибутив. Нельзя
выдавать тот же tester1 параллельно телефонным/двухклиентным QA-прогонам.

Launcher получает свежий Photon token через PlayFab Client API при каждом
запуске; серверные ключи и GatewayKey ему не нужны. Runtime auth/status/resume
живут под LocalAppData с ACL текущий пользователь+SYSTEM; значения credentials
не выводятся. Перед запуском выполняется проверка ожидаемой identity.

Код комнаты — текущий `instanceId`, показываемый строкой «Экземпляр» панели
Server Manager. Сервер владельца должен быть Ready. Пользователь может ввести
новый код после рестарта без пересборки ZIP. Для игрового подключения не нужен
доступ тестера к loopback панели; используется существующий Photon relay path.
Это не отменяет 30-минутный QA lifetime, drain последних 5 минут, allowlist и
существующий VPN-регламент. Автозапуска сервера/матча/выбора усилений нет.

## Проверка и границы

Пакет сканируется на известные owner/server credentials до архивирования;
персональный tester1 credential намеренно разрешён. Проверка после распаковки
вне проекта должна подтвердить отсутствие .NET SDK/repo/owner-private dependencies,
актуальный auth, подключение к существующему серверу и загрузку MainMenu.
Такая проверка на этом ПК не является проверкой сети и устройства внешнего тестера.

## Пакет 17.09

Архив: `Builds/Distributions/TankDraft-PC-Tester01-20260917-153000.zip`,
43 658 212 байт, SHA256 `1ea5269370a077928b777610d9443d46891841a941306fc53ac83e91e55d4ba8`.
284 ZIP entries; owner launcher/Client.rar/логи/runtime auth/gateway отсутствуют.
Template hashes Launch/StartGame/README совпадают после распаковки. Secret scan
проверил owner CustomID, PlayFab server key/server identity и сохранённые Photon
tokens tester0/tester1/server: совпадений нет. Personal tester1 CustomID разрешён
намеренно; это credential тестового аккаунта, а не анонимный публичный пакет.

Распаковка проверена в `C:/Users/ramze/AppData/Local/Temp/TankDraft Tester QA 70d7f57b064c408eab7a493ecda1d6c1`.
Cold Windows PowerShell 5.1 + copied StartGame.cmd получили новый Photon token,
запустили именно этот Game exe, `FUSION_CLIENT_CONNECTED` и MainMenu Status Idle.
Invalid server code отклонён до auth; повторный запуск отклонён до auth/Player.
Первоначальная команда автоматизации с кавычками пути была исправлена запуском
из extraction working directory; это не ошибка файла StartGame.cmd.

PackageId `9a353ab74565470eb9c434b14bb381d5`; runtime evidence под
`%LOCALAPPDATA%/TankDraft/TesterQA/<PackageId>/1de94feae26c4c7793efb79050aafcb3/Logs`.
Normal launcher не включает автоматику. Дополнительный операторский запуск
распакованного exe с существующими QA autoqueue/choice env подтвердил серверный
поиск10s → Bot → Draft/Battle → MatchResult round4, счёт0:4. Последний snapshot:
Connections1, Connected=true, Pending=false. Тестовый Player завершён; сервер
Ready, queued0/activeMatches0, аккаунт свободен для внешнего тестера. В журнале
автоматического выхода есть FUSION_RECONNECT_BEGIN перед shutdown; во время
самого матча переподключений не зафиксировано. Это не внешняя сетевая приёмка.

Для запуска владельцем серверной части — существующая локальная панель.
Код в этом архиве `f2fcb8e2387a4ee596996a0ff0d1d794`: он действителен только до
рестарта/остановки данного QA server instance. Новый код передаётся тестеру
отдельно, обновление самой сборки для этого не требуется.
