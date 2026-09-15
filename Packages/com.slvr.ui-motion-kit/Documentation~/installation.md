# Установка

1. Установить официальный Free DOTween в host project.
2. Открыть `Tools > Demigiant > DOTween Utility Panel` и выполнить `Setup DOTween...`.
3. Убедиться, что создан `Assets/Resources/DOTweenSettings.asset`.
4. Пакет подключает `DOTween.dll` как precompiled reference; DOTween asmdef и shortcut modules для
   Phase 1 не обязательны.
5. Выполнить `Tools > SLVR > UI Motion > Create or Repair Default Assets`.
6. Запустить EditMode и PlayMode tests с префиксом `SLVR.UIMotion`.

DOTween Pro не требуется. DOTween остаётся зависимостью host project и не копируется в пакет.
