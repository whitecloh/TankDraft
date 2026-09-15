using System;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Presentation settings")]
    public sealed class BattlePresentationSettings : ScriptableObject
    {
        [SerializeField]
        private Color _side0Color = Color.blue, _side1Color = Color.red;
        [SerializeField]
        private int _prewarmUnitsPerPrefab = 24, _prewarmProjectilesPerPrefab = 64, _prewarmZonesPerPrefab = 24, _prewarmEffects = 48, _maxPoolPerPrefab = 256, _maxTicksPerFrame = 8;
        [SerializeField]
        private float _effectSeconds = .22f;
        [SerializeField]
        private float _minimumEffectRadius = .24f;
        [SerializeField]
        private float _unitSpawnSeconds = .22f, _unitSpawnStartScale = .72f, _unitSpawnOvershoot = 1.05f, _unitSpawnStagger = .035f, _unitSpawnMaxDelay = .18f;
        [SerializeField, Min(.01f)] private float _formationMoveSeconds = .25f;
        [SerializeField, Min(.01f)] private float _hitFlashSeconds = .08f;
        [SerializeField, Min(.01f)] private float _transformationSeconds = .25f;
        [SerializeField]
        private Vector2 _debugZonePosition = new Vector2(0f, .8f);
        [SerializeField]
        private string _title = "TANK DRAFT / БОЕВОЙ ПРОТОТИП", _ready = "Готов к запуску", _running = "Бой", _paused = "Пауза", _side0Won = "Синяя армия победила", _side1Won = "Красная армия победила", _reviewRequired = "Одновременное уничтожение: нужен разбор", _start = "Старт", _pause = "Пауза / далее", _step = "Шаг", _reset = "Заново", _scenario = "Состав", _debugZone = "Тест зоны", _statsFormat = "Юниты {0} • Снаряды {1} • Зоны {2}", _timeFormat = "{0:0.0} с • тик {1}", _armyFormat = "{0} в строю", _legend = "МИНЫ → ТАНКИ → ПТ → АРТИЛЛЕРИЯ";
        public Color Side0Color => _side0Color;
        public Color Side1Color => _side1Color;
        public int PrewarmUnitsPerPrefab => _prewarmUnitsPerPrefab;
        public int PrewarmProjectilesPerPrefab => _prewarmProjectilesPerPrefab;
        public int PrewarmZonesPerPrefab => _prewarmZonesPerPrefab;
        public int PrewarmEffects => _prewarmEffects;
        public int MaxPoolPerPrefab => _maxPoolPerPrefab;
        public int MaxTicksPerFrame => _maxTicksPerFrame;
        public float EffectSeconds => _effectSeconds;
        public float MinimumEffectRadius => _minimumEffectRadius;
        public float UnitSpawnSeconds => _unitSpawnSeconds;
        public float UnitSpawnStartScale => _unitSpawnStartScale;
        public float UnitSpawnOvershoot => _unitSpawnOvershoot;
        public float UnitSpawnStagger => _unitSpawnStagger;
        public float UnitSpawnMaxDelay => _unitSpawnMaxDelay;
        public float FormationMoveSeconds => _formationMoveSeconds;
        public float HitFlashSeconds => _hitFlashSeconds;
        public float TransformationSeconds => _transformationSeconds;
        public Vector2 DebugZonePosition => _debugZonePosition;
        public string Title => _title;
        public string Ready => _ready;
        public string Running => _running;
        public string Paused => _paused;
        public string Side0Won => _side0Won;
        public string Side1Won => _side1Won;
        public string ReviewRequired => _reviewRequired;
        public string Start => _start;
        public string Pause => _pause;
        public string Step => _step;
        public string Reset => _reset;
        public string Scenario => _scenario;
        public string DebugZone => _debugZone;
        public string StatsFormat => _statsFormat;
        public string TimeFormat => _timeFormat;
        public string ArmyFormat => _armyFormat;
        public string Legend => _legend;

        public void Validate()
        {
            if (!PositiveFinite(_formationMoveSeconds) || !PositiveFinite(_hitFlashSeconds) || !PositiveFinite(_transformationSeconds)) throw new InvalidOperationException(name + " has invalid presentation duration.");
            if (_prewarmUnitsPerPrefab < 0 || _prewarmProjectilesPerPrefab < 0 || _prewarmZonesPerPrefab < 0 || _prewarmEffects < 0 || _maxPoolPerPrefab < 1 || _maxTicksPerFrame < 1 || !PositiveFinite(_effectSeconds) || !PositiveFinite(_minimumEffectRadius) || !PositiveFinite(_unitSpawnSeconds) || !PositiveFinite(_unitSpawnStartScale) || !PositiveFinite(_unitSpawnOvershoot) || !NonNegativeFinite(_unitSpawnStagger) || !NonNegativeFinite(_unitSpawnMaxDelay) || _prewarmUnitsPerPrefab > _maxPoolPerPrefab || _prewarmProjectilesPerPrefab > _maxPoolPerPrefab || _prewarmZonesPerPrefab > _maxPoolPerPrefab || _prewarmEffects > _maxPoolPerPrefab)
                throw new InvalidOperationException(name + " has invalid pooling settings.");
        }

        private static bool PositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool NonNegativeFinite(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
