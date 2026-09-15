# Зависимости
Status: base packages resolved and compiled; service integration pending
Last reviewed: 2026-09-13

| Компонент | Решение |
|---|---|
| Unity/URP | 6000.3.10f1 / 17.3.0, уже в проекте |
| LeoECS Lite | Закреплён d46f3b209ea163810823aeea7d2610d38ad6cdf8 |
| UniTask | Закреплён e5acc106ee196bc5a32fb14cdf2987b0f96d11e0 |
| DOTween Free | Уже установлен; сохранить пользовательские изменения |
| Unity MCP | Core 0.89.0; локальный endpoint 21509, проверен |
| Localization | Unity Localization 1.5.9 установлен; Addressables подключён транзитивно |
| Newtonsoft JSON | 3.2.2, явная runtime-зависимость Infrastructure для строгой проверки локального сохранения; ранее уже был установлен транзитивно |
| UI authoring | UGUI + native Unity Editor tooling |
| VContainer/MessagePipe | 1.18.0 / 1.8.1 + MessagePipe.VContainer 1.8.1, OpenUPM, сборки загружены |
| UI Motion | Embedded com.slvr.ui-motion-kit 0.10.6 из Chibi; DOTween Free, UGUI; optional samples не импортированы |
| Foundation | Принципы перенесены; код модулей добавлять с первыми потребителями, не копировать целиком |
| PUN 2 | Legacy opt-in `TANKDRAFT_PUN_PROBE`, по умолчанию выключен: 9 asmdef + 4 DLL importers. Core SDK 2.50/GUIDs и прежние пробы сохранены. Reversible меню TankDraft/Backend/Legacy PUN Probe; новый active server path не использует Photon |
| Odin/Sirenix | Исключены; не импортировать |
| FMOD/Spine/SRDebugger | Не обязательны для bootstrap; выбор по реальным задачам/доступному SDK |
| PlayFab Unity | Classic UnitySDK 2.242.260805, официальный unitypackage, Assets/PlayFabSDK; autoReferenced=false, authored settings в Configs/Infrastructure/PlayFab/Resources, TitleId B16D9 (Development по скриншоту владельца), пустой secret key и выключенная телеметрия. Реальный login не выполнялся. Unified v2 пока не поддерживает Android/iOS. См. Docs/Features/PlayFab_SDK_Setup.md |
| PlayFab server | Backend/TankDraft.Server.PlayFab: PlayFabAllSDK 1.229.260805, com.playfab.csharpgsdk 0.11.210519, Newtonsoft.Json 13.0.4; NuGet lockfile, сборка и vulnerability scan проверены. Local GSDK probe проверен отдельно; реальный login/production host ещё впереди |
| LocalMultiplayerAgent | Dev-tool вне Assets в Logs/PlayFabLma: upstream v0.12.0-beta / a7af68aea68ee73df85ea63ce81692ba439ff476, одна поправка listener на loopback; bootstrap-lma.ps1, test-gsdk-lifecycle.ps1. Не заменяет cloud acceptance |
| Azure | `Backend/TankDraft.Server.Azure`: Azure.Data.Tables 12.12.0 + Azure.Storage.Blobs 12.29.2, pinned locks, write adapters и offline checks. Cloud account/auth/storage/recovery не проверены; Functions/Identity/Queues SDK пока без потребителя |
| PlayFab Identity | `Backend/TankDraft.Server.PlayFab.Identity`: PlayFabAllSDK 1.229.260805 + Newtonsoft.Json 13.0.4 без GSDK; verifier session ticket и offline checks; не подключён к публичному auth route |
| Edgegap | `Backend/TankDraft.Server.Edgegap`: .NET HttpClient REST v2 deploy/v1 status-stop; Unity SDK не требуется. Readiness host/container отдельно от LocalHost. См. Docs/Features/Edgegap_Integration.md |
| Local server persistence | Microsoft.Data.Sqlite 10.0.12 через NuGet только в Backend/TankDraft.Server.Persistence, packages.lock.json и locked restore; Unity UPM/Assets не меняются. WAL/FULL и локальное восстановление проверены; это не Azure storage adapter |
| IAP | PlayFab/Azure выбран как backend для валидации и выдачи; конкретный Unity IAP SDK — кандидат, не установлен и не проверен |
| Ads | Провайдер не выбран; SDK не установлен |

Пакет, объявленный в manifest, не считается успешно подключённым до UPM resolve и компиляции.
Не копировать Chibi credentials, PlayFab title IDs, Figma/Asana IDs или полный набор art examples.

PUN: Unity API Updater адаптировал PhotonRigidbody2DView.cs и PhotonRigidbodyView.cs к Unity 6. Core/Editor зависимости проверены по asmdef. Штатный ImportPackage выбранного subset не создал файлы; оригинальные asset/meta восстановлены через MCP и AssetDatabase.Refresh с сохранением GUID. Исходный AssetStore архив не изменён. SDK версии и diff учитывать при будущем обновлении.
Финальный арт/UI pack ещё не выбран. Canvas-превиз не заменяет оценку силуэтов, обводки и детализации техники по видео. FMOD/Spine/IAP/Ads не импортировать ради формальной полноты без задачи и провайдера.
