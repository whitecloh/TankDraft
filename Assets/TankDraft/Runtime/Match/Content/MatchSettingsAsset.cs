using System;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Content;
using TankDraft.Infrastructure;
using TankDraft.Match.Domain;
using UnityEngine;

namespace TankDraft.Match.Content
{
    [CreateAssetMenu(menuName = "TankDraft/Match/Settings")]
    public sealed class MatchSettingsAsset : ScriptableObject
    {
        [SerializeField]
        private BattleScenarioCatalogAsset _battleCatalog;
        [SerializeField]
        private BattleViewCatalogAsset _views;
        [SerializeField]
        private BattlePresentationSettings _battlePresentation;
        [SerializeField]
        private MetaCatalogAsset _metaCatalog;
        [SerializeField]
        private LocalProfileSettings _profileSettings;
        [SerializeField]
        private MatchTextAsset _text;
        [SerializeField]
        private string _menuSceneName = "MainMenu";
        [SerializeField]
        private string _matchSceneName = "Match";
        [SerializeField]
        private uint _seed = 612;
        [SerializeField, Min(1)]
        private int _winsRequired = 4;
        [SerializeField, Min(1)]
        private int _normalChoices = 3;
        [SerializeField, Min(1)]
        private int _maxUnitsPerType = 12;
        [SerializeField, Min(1)]
        private int _upgradeHpPercent = 15;
        [SerializeField, Min(1)]
        private int _upgradeDamagePercent = 12;
        [SerializeField, Min(0)]
        private int _orderCharges = 3;
        [SerializeField, Min(0)]
        private int _orderArmorPercent = 25;
        [SerializeField, Min(1)]
        private int _addWeight = 60;
        [SerializeField, Min(1)]
        private int _doubleWeight = 28;
        [SerializeField, Min(1)]
        private int _upgradeWeight = 12;
        [Serializable]
        public struct DraftUnitEntry
        {
            public BattleUnitAsset unit;
            [Min(1)]
            public int addCount;
        }

        [SerializeField]
        private DraftUnitEntry[] _units;
        public BattleScenarioCatalogAsset BattleCatalog => _battleCatalog;
        public BattleViewCatalogAsset Views => _views;
        public BattlePresentationSettings BattlePresentation => _battlePresentation;
        public MetaCatalogAsset MetaCatalog => _metaCatalog;
        public LocalProfileSettings ProfileSettings => _profileSettings;
        public MatchTextAsset Text => _text;
        public string MenuSceneName => _menuSceneName;
        public string MatchSceneName => _matchSceneName;
        public uint Seed => _seed;
        public string LocalBot => TextValue(x => x.LocalBot);
        public string RoundFormat => TextValue(x => x.RoundFormat);
        public string ScoreFormat => TextValue(x => x.ScoreFormat);
        public string DraftFormat => TextValue(x => x.DraftFormat);
        public string Comeback => TextValue(x => x.Comeback);
        public string Waiting => TextValue(x => x.Waiting);
        public string Battle => TextValue(x => x.Battle);
        public string RoundWin => TextValue(x => x.RoundWin);
        public string RoundLoss => TextValue(x => x.RoundLoss);
        public string MatchWin => TextValue(x => x.MatchWin);
        public string MatchLoss => TextValue(x => x.MatchLoss);
        public string ReviewRequired => TextValue(x => x.ReviewRequired);
        public string Continue => TextValue(x => x.Continue);
        public string Menu => TextValue(x => x.Menu);
        public string OrderFormat => TextValue(x => x.OrderFormat);
        public string NoOrder => TextValue(x => x.NoOrder);
        public string AddFormat => TextValue(x => x.AddFormat);
        public string DoubleFormat => TextValue(x => x.DoubleFormat);
        public string UpgradeFormat => TextValue(x => x.UpgradeFormat);
        public string ArmyFormat => TextValue(x => x.ArmyFormat);
        public string NoArmy => TextValue(x => x.NoArmy);
        public string UnsupportedDeck => TextValue(x => x.UnsupportedDeck);
        public string OrderHint => TextValue(x => x.OrderHint);

        public MatchRules CreateRules() => new MatchRules(_winsRequired, _normalChoices, _maxUnitsPerType, _upgradeHpPercent, _upgradeDamagePercent, _orderCharges, _orderArmorPercent, _addWeight, _doubleWeight, _upgradeWeight);
        public MatchUnitDefinition[] CreateUnits()
        {
            if (_units == null || _units.Length < 4)
                throw new InvalidOperationException(name + " requires at least four authored draft unit entries.");
            var result = new MatchUnitDefinition[_units.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var entry = _units[i];
                if (!entry.unit || entry.addCount < 1)
                    throw new InvalidOperationException(name + " has an invalid draft unit reference/count.");
                var definition = entry.unit.CreateDefinition();
                bool mobileCombatant = definition.MoveSpeed > 0 &&
                    (definition.Attack == TankDraft.Contracts.Battle.BattleAttackKind.Projectile ||
                     definition.Attack == TankDraft.Contracts.Battle.BattleAttackKind.Melee);
                result[i] = new MatchUnitDefinition(definition.Id, entry.addCount, mobileCombatant);
            }

            return result;
        }

        public void Validate()
        {
            if (_battleCatalog == null || _views == null || _battlePresentation == null || _metaCatalog == null || _profileSettings == null || _text == null)
                throw new InvalidOperationException(name + " has incomplete authored references.");
            if (string.IsNullOrWhiteSpace(_menuSceneName) || string.IsNullOrWhiteSpace(_matchSceneName))
                throw new InvalidOperationException(name + " requires scene names.");
            _battleCatalog.Validate();
            var definitions = _battleCatalog.CreateDefinitions();
            _views.Validate(definitions);
            _battlePresentation.Validate();
            _metaCatalog.CreateDefinitions();
            CreateRules();
            MatchUnitDefinition[] units = CreateUnits();
            for (var index = 0; index < units.Length; index++)
            {
                _metaCatalog.GetVisual(units[index].Id);
                definitions.Unit(units[index].Id);
            }
        }

        private string TextValue(Func<MatchTextAsset, string> get)
        {
            if (_text == null)
                throw new InvalidOperationException(name + " requires match text.");
            return get(_text);
        }
    }
}
