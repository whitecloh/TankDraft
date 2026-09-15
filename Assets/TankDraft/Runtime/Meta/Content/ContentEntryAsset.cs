using UnityEngine;
using TankDraft.Contracts;

namespace TankDraft.Content
{
    [CreateAssetMenu(menuName = "TankDraft/Content/Collection entry")]
    public sealed class ContentEntryAsset : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private ContentKind _kind;
        [SerializeField] private FormationRow _row;
        [SerializeField, Min(1)] private int _requiredArena = 1;
        [SerializeField] private string _title;
        [SerializeField, TextArea] private string _description;
        [SerializeField] private string _roleLabel;
        [SerializeField] private Sprite _icon;
        public string Id => _id;
        public string Title => _title;
        public string Description => _description;
        public string RoleLabel => _roleLabel;
        public Sprite Icon => _icon;
        public ContentDefinition CreateDefinition() => new ContentDefinition(_id, _kind, _row, _requiredArena);
        public void Validate()
        {
            CreateDefinition();
            if (string.IsNullOrWhiteSpace(_title) || string.IsNullOrWhiteSpace(_description) || string.IsNullOrWhiteSpace(_roleLabel))
                throw new System.InvalidOperationException(name + " requires title, description and role label.");
        }
    }
}
