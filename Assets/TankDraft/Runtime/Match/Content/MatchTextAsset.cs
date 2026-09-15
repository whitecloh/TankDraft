using UnityEngine;

namespace TankDraft.Match.Content
{
    [CreateAssetMenu(menuName = "TankDraft/Match/Text")]
    public sealed class MatchTextAsset : ScriptableObject
    {
        [SerializeField]
        private string _localBot = "Локальный бот";
        [SerializeField]
        private string _roundFormat = "Раунд {0}";
        [SerializeField]
        private string _scoreFormat = "{0} : {1} · до {2} побед";
        [SerializeField]
        private string _draftFormat = "Выбор {0}/{1}";
        [SerializeField]
        private string _comeback = "Дополнительный выбор за проигранный раунд";
        [SerializeField]
        private string _waiting = "Ожидание выбора";
        [SerializeField]
        private string _battle = "БОЙ";
        [SerializeField]
        private string _roundWin = "Раунд выигран";
        [SerializeField]
        private string _roundLoss = "Раунд проигран";
        [SerializeField]
        private string _matchWin = "Матч выигран";
        [SerializeField]
        private string _matchLoss = "Матч проигран";
        [SerializeField]
        private string _reviewRequired = "Требуется проверка результата";
        [SerializeField]
        private string _continue = "ДАЛЕЕ";
        [SerializeField]
        private string _menu = "В МЕНЮ";
        [SerializeField]
        private string _orderFormat = "Броня +{1}% HP · {0}";
        [SerializeField]
        private string _noOrder = "Без приказа";
        [SerializeField]
        private string _addFormat = "+{0}";
        [SerializeField]
        private string _doubleFormat = "×2 · {0}";
        [SerializeField]
        private string _upgradeFormat = "Улучшение · ур. {0}";
        [SerializeField]
        private string _armyFormat = "Армия: {0}";
        [SerializeField]
        private string _noArmy = "Армия не собрана";
        [SerializeField]
        private string _unsupportedDeck = "Для этого прототипа выберите мины, тяжёлый танк, ПТ-САУ и полевую артиллерию. Поддерживается приказ «Усилить броню»; остальные слоты приказов оставьте пустыми.";
        [SerializeField]
        private string _orderHint = "Выберите одну карточку. Приказ заменяет текущий выбор.";
        public string LocalBot => _localBot;
        public string RoundFormat => _roundFormat;
        public string ScoreFormat => _scoreFormat;
        public string DraftFormat => _draftFormat;
        public string Comeback => _comeback;
        public string Waiting => _waiting;
        public string Battle => _battle;
        public string RoundWin => _roundWin;
        public string RoundLoss => _roundLoss;
        public string MatchWin => _matchWin;
        public string MatchLoss => _matchLoss;
        public string ReviewRequired => _reviewRequired;
        public string Continue => _continue;
        public string Menu => _menu;
        public string OrderFormat => _orderFormat;
        public string NoOrder => _noOrder;
        public string AddFormat => _addFormat;
        public string DoubleFormat => _doubleFormat;
        public string UpgradeFormat => _upgradeFormat;
        public string ArmyFormat => _armyFormat;
        public string NoArmy => _noArmy;
        public string UnsupportedDeck => _unsupportedDeck;
        public string OrderHint => _orderHint;
    }
}
