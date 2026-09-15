# PlayFab SDK: установка и границы подключения
Уточнение 2026-09-14: Unity PlayFabSDK и Server.PlayFab.Identity сохранены для Fusion Dedicated. Старый Server.PlayFab (AllSDK/GSDK probe), GsdkLocalProbe и команды их проверки удалены как неиспользуемые. Актуальный сетевой этап: [Fusion_Dedicated_Migration.md](Fusion_Dedicated_Migration.md); дальнейшая интеграция — Photon Custom Authentication с PlayFab, без выдачи server secret клиентам.
Status: PlayFab identity/admission/Unity login components installed; live server identity/admission PASS 2026-09-13; Unity device login and remote PvP pending
Last reviewed: 2026-09-13

## Проект PlayFab и сведения владельца

Владелец передал Title ID **B16D9**. По двум предоставленным скриншотам: studio **My Game Studio**, title **Tanks**, режим **Development**. ID внесён в authored Unity settings через MCP; DeveloperSecretKey пуст, login/API вызовы не выполнялись.

Billing Summary показывает **September 2026 estimated costs**, **All titles**, month-to-date **$0.00**, последнее обновление **2026-09-12**. Видна строка Profile: Storage / Development mode / 0.22 KB / Free. Это предоставленный снимок оценки расходов всей студии, не live-проверка аккаунта, не итоговый счёт и не доказательство отсутствия начислений MPS/Azure. Название платного плана, статус MPS и принудительная остановка потребления на лимите на скриншотах не показаны.

Карточка title показывает `0 / 100K unique players`. Актуальная публичная документация Development Mode указывает 1000 lifetime account creations; расхождение интерфейса и документации не разрешено. Не использовать 100K как подтверждённую квоту или CCU. [Development Mode](https://learn.microsoft.com/en-us/xbox/playfab/pricing/development-mode). MPS имеет отдельный учёт compute/egress и free evaluation allowance; zero-charge cloud gate остаётся закрытым. [MPS billing](https://learn.microsoft.com/en-us/xbox/playfab/multiplayer/servers/billing-for-thunderhead).

## Установлено

Актуальная реализация login/session и безопасный локальный ввод server key: [PlayFab_Admission.md](PlayFab_Admission.md). Unity login source использует instance SDK; server verifier — те же SDK DTO через отдельный bounded HTTP transport. 13.09.2026 реальный B16D9 live probe подтвердил server identity/admission: два players, client login, server verification, assigned seats, lost-reply retry и reconnect rotation, 10 API calls без tickets/account IDs в output. Unity device login, remote PvP и cloud composition этим не проверены.

| Слой | Зависимость | Версия / расположение |
|---|---|---|
| Unity Android/iOS client | Официальный classic UnitySDK | 2.242.260805, `Assets/PlayFabSDK`, сборка `PlayFab` |
| Серверные API | PlayFabAllSDK | 1.229.260805, активный `Backend/TankDraft.Server.PlayFab.Identity`; прежний Server.PlayFab только MPS probe |
| MPS lifecycle | com.playfab.csharpgsdk | 0.11.210519, тот же отдельный серверный проект |
| GSDK serialization dependency | Newtonsoft.Json | Явно закреплена 13.0.4 вместо старой минимальной зависимости GSDK 11.0.2 |

SDK версии и зависимости закреплены в `packages.lock.json`; единственная дополнительная транзитивная зависимость текущего server graph — Polly 7.2.3. [PlayFabAllSDK](https://www.nuget.org/packages/PlayFabAllSDK/1.229.260805), [официальный C# GSDK](https://github.com/PlayFab/gsdk/tree/main/csharp), [NuGet GSDK](https://www.nuget.org/packages/com.playfab.csharpgsdk/0.11.210519).

Серверный проект содержит SDK boundary и LocalGsdkBoundary. Он не включён в LocalHost. Отдельный [GsdkLocalProbe](Gsdk_Local_Lifecycle.md) проверяет настоящий GSDK с локальным агентом и DurableMatch; production service adapter ещё впереди. `SdkAvailability` обращается только к сведениям о типах/сборках.

Добавлены Azure.Data.Tables 12.12.0 и Azure.Storage.Blobs 12.29.2 в отдельный `TankDraft.Server.Azure`, с write adapters и offline SDK checks. PlayFab.Identity использует per-instance AuthenticateSessionTicket без GSDK. Edgegap — server-only REST adapter; Unity hosting plugin не нужен pure .NET серверу. Functions/Identity/Queues SDK появятся с потребителями. Актуальные проверки/ограничения: [Edgegap_Integration.md](Edgegap_Integration.md).

## Выбор Unity SDK

Новый **PlayFab for Unity v2 Unified** пока официально поддерживает Windows/Xbox, остальные платформы заявлены как будущие. Поэтому для Android/iOS установлен classic UnitySDK; число `2.242` в его версии не означает Unified v2. Проверить поддержку мобильных платформ снова перед будущей миграцией. [Поддерживаемые платформы Unified](https://learn.microsoft.com/en-us/xbox/playfab/sdks/unified-unity/overview).

Импорт выполнен через Unity MCP → `AssetDatabase.ImportPackage`, по [официальному способу UnitySDK](https://github.com/PlayFab/UnitySDK/blob/2.242.260805/UnityGettingStarted.md). Использован только `UnitySDK.unitypackage`, без Editor Extensions, PaperTrail и второго Json.NET wrapper.

Источник: [release 2.242.260805](https://github.com/PlayFab/UnitySDK/releases/tag/2.242.260805), commit `d9c3ae9aa2f4138c74674f3d9e4175d0a56a66f5`.
SHA-256 архива: `54e24792f655dd010117321cff23fef76eb822956610bf6748c454ecf399431e`.
Архив/инспекция сохранены в ignored `Logs/SdkDownloads`. Apache-2.0 LICENSE из того же commit добавлен в `Assets/PlayFabSDK/LICENSE.txt`; исходные GUID импортированных файлов сохранены.

Локальные изменения после импорта:

- `PlayFab.asmdef`: `autoReferenced=false`. Будущий Unity infrastructure adapter должен явно ссылаться на `PlayFab`; боевые contracts/domain/simulation не получают SDK.
- Единственный `PlayFabSharedSettings.asset` перенесён через `AssetDatabase.MoveAsset` с сохранением GUID в `Assets/TankDraft/Configs/Infrastructure/PlayFab/Resources`.
- TitleId **B16D9** передан владельцем и заполнен локально. DeveloperSecretKey пуст. Отключены device info, focus time collection и realtime logging. ProductionEnvironmentUrl оставлен пустым; запросы не выполняются.
- Server/admin/secret-key scripting defines отсутствуют для Standalone/Android/iOS; серверные и admin API не скомпилированы в текущую Unity assembly.

При обновлении SDK повторно применить эти изменения и проверить отсутствие второго Resources/PlayFabSharedSettings. Пункт SDK MakePlayFabSharedSettings создаёт asset по исходному vendor path — для проекта следует редактировать существующий authored asset, а не создавать дубликат.

TitleId — публичный идентификатор проекта, он уже заполнен. Это не авторизация клиента и не разрешение серверных API. DeveloperSecretKey не должен попадать в Unity settings, клиент или Git даже для теста. Чужие IDs/ключи из Chibi Arena не переносились.

## Проверка

- `./Tools/Backend/verify-playfab-sdk.ps1`: locked restore, сборка net10 с нулём warnings/errors, NuGet audit текущих источников без известных уязвимых зависимостей. Это не полный security audit.
- `Tools/Backend/VerifyUnityPlayFabSdk.cs` выполнить через MCP, class `TankDraftUnityPlayFabSdkValidation`, method `Run`. Проверены загруженная версия, наличие client API/отсутствие server/admin API, единственные settings, отсутствие ключа/опасных defines, выключенная телеметрия и explicit assembly references.
- Unity 6000.3.10f1 завершил компиляцию. Одно предупреждение поставщика CS0618 в `Shared/Internal/SingletonMonoBehaviour.cs:24`: FindObjectOfType устарел в Unity 6. Vendor implementation не переписывалась ради подавления warning.
- MainMenu осталась в Edit Mode, `scene dirty=false`. Нет PlayFab login, GSDK heartbeat, назначения матча или вызовов реальной экономики.

Не проверены Android/iOS player builds, IL2CPP/device runtime, Unity device login, Linux/MPS и облачные API. Live server identity/admission выполнена отдельно, но production adapters и сетевой перенос Unity-клиента ещё впереди. Локальный GSDK lifecycle теперь проверен отдельно: [Gsdk_Local_Lifecycle.md](Gsdk_Local_Lifecycle.md).

## Следующий шаг

Текущий порядок заменён [Functional_Release_Plan.md](Functional_Release_Plan.md) и [Edgegap_Integration.md](Edgegap_Integration.md): Edgegap container smoke → real identity/admission/durability → remote PvP. Ниже историческая запись после установки первого SDK.

Локальный GSDK spike выполнен; Title ID и режим title подтверждены владельцем/скриншотом, условия MPS billing остаются непроверенными. Далее Unity transport/auth interface и два клиента с reconnect/resync. Реальный login и облачное размещение имеют отдельные проверки. Установка пакетов, заполнение ID и локальный тест не открывают [zero-charge gate](Backend_Implementation_Plan.md) и не создают PlayFab/Azure ресурсы.
