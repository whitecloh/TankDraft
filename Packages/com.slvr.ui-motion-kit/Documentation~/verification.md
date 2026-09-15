# Verification matrix

## Automated

- EditMode: settings/tokens/ease/budget/formatter/material cache/integration contracts.
- PlayMode: lifecycle/reentry/input/panel/idle/attention/shader/badge/value/progress/pool/currency/reward skip,
  включая стабильную hit-area и плавный выход `UiHoverLiftMotion.ScaleWave` без snap.
- Ожидается zero failures; baseline project failures отделяются от package failures.

## Manual Editor

- Import gallery, open sample scene, switch themes/quality/intensity/Reduced Motion.
- Run UI audit and inspect findings; do not apply fixes without opt-in.
- Verify stencil masks and material lease counts after disable/destroy.

## Device gates

- Android touch, burst pool return, CPU/GC/Canvas.BuildBatch/overdraw at 60 Hz and 30 FPS cap.
- Steam mouse/keyboard/gamepad focus and submit.
- Clean Unity `6000.3` import with DOTween prerequisite and documented actionable error without DOTween.

Неподключённое устройство, отсутствие clean test project или contaminated Editor allocation capture означает `PENDING`, а не PASS.
