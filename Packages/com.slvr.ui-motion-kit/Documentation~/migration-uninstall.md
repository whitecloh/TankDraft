# Migration и uninstall

## Migration

Сначала подключайте package к одному окну через host bridge. Не меняйте gameplay ViewModel/presenter/commands. Сохраните baseline screenshots и profiler capture, затем добавляйте VisualRoot и motion components по одному слою: structural → interaction → ambient → celebration.

При обновлении package проверьте `CHANGELOG.md`, full EditMode/PlayMode suite, prefab overrides и Reduced Motion. DOTween остаётся host dependency и не копируется внутрь package.

## Uninstall

1. Удалите host bridge/components и imported gallery sample из `Assets/Samples`, сохранив gameplay wiring.
2. Удалите embedded package entry/папку.
3. DOTween удаляйте отдельно только если его не используют другие системы.
4. Удалите созданный global settings asset только после проверки references.

Package не создаёт PlayerPrefs, save schema, analytics events, Addressables groups или production settings, поэтому данных для миграции нет.
