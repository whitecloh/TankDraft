using System;
using UnityEngine;
using TankDraft.Contracts;
namespace TankDraft.Content
{
    [CreateAssetMenu(menuName = "TankDraft/Content/Meta catalog")]
    public sealed class MetaCatalogAsset : ScriptableObject
    {
        [SerializeField] private ContentEntryAsset[] _entries;
        [SerializeField] private ArmyRulesAsset _armyRules;
        [SerializeField] private NewProfileAsset _newProfile;
        [SerializeField] private UiTextCatalog _uiText;
        [SerializeField] private Sprite[] _currencyIcons = new Sprite[4];
        [SerializeField, Min(1)] private int _arenaTarget = 200;
        [SerializeField, Range(1000, 10000)] private int _resultRefreshMilliseconds = 2000;
        [SerializeField, Range(1, 5)] private int _resultRefreshAttempts = 3;
        [SerializeField] private TextAsset _progressionRules;
        public UiTextCatalog Text => _uiText;
        public int ArenaTarget => _arenaTarget;
        public int ResultRefreshMilliseconds => _resultRefreshMilliseconds;
        public int ResultRefreshAttempts => _resultRefreshAttempts;
        // This is display content only. The remote authority verifies the same version
        // before it accepts a progression intent.
        public TextAsset ProgressionRules => _progressionRules;
        public Sprite CurrencyIcon(int index) => _currencyIcons[index];
        public ProfileSnapshot CreateInitialProfile() => _newProfile.CreateSnapshot();
        public ContentEntryAsset GetVisual(string id)
        {
            foreach (var entry in _entries) if (entry.Id == id) return entry;
            throw new InvalidOperationException("Missing visual: " + id);
        }
        public MetaDefinitions CreateDefinitions()
        {
            if (_entries == null || _entries.Length == 0 || !_armyRules || !_newProfile || !_uiText || _currencyIcons == null || _currencyIcons.Length != 4 || _arenaTarget < 1)
                throw new InvalidOperationException("Meta catalog has incomplete references.");
            _uiText.Validate();
            if (_resultRefreshMilliseconds < 1000 || _resultRefreshMilliseconds > 10000 || _resultRefreshAttempts < 1 || _resultRefreshAttempts > 5)
                throw new InvalidOperationException("Invalid result refresh limits.");
            var definitions = new ContentDefinition[_entries.Length];
            for (var i = 0; i < definitions.Length; i++)
            {
                if (!_entries[i]) throw new InvalidOperationException("Null collection entry.");
                _entries[i].Validate(); definitions[i] = _entries[i].CreateDefinition();
            }
            return new MetaDefinitions(definitions, _armyRules.CreateDefinition());
        }
    }
}
