# Audio, haptics и focal-art adapters

`IUiMotionAudioFeedback` принимает semantic `UiMotionAudioCue`. `UiAudioSourceFeedbackAdapter` — optional bridge; package не поставляет и не назначает игровые clips.

`IUiMotionHaptics` принимает `UiMotionHapticCue`. Host реализует интерфейс поверх выбранного Android/iOS/Steam SDK и передаёт provider в `UiMotionHapticsRelay`. Core не зависит от стороннего haptics SDK.

`IUiShowcaseAdapter` предназначен только для одного focal character/art object. `UiAnimatorShowcaseAdapter` запускает entrance/idle/selection states. Обычные UI transforms Animator не используют.

Spine adapter находится в Package Manager sample `Spine Integration` и отдельной assembly `SLVR.UIMotion.Spine`. Импортируйте sample только при наличии `spine-unity`; animation names валидируются при вызове. Core assembly Spine не reference’ит.
