using UnityEngine;

namespace TankDraft.Match.ServerClient
{
    [CreateAssetMenu(menuName = "TankDraft/Match/Network Queue Settings")]
    public sealed class NetworkQueueSettings : ScriptableObject
    {
        public string MatchSceneName = "ServerMatch";
        public string ContentVersion;
        [Range(500, 5000)] public int PollMilliseconds = 1000;
        public string SearchTitle = "Поиск соперника";
        public string SearchFormat = "Ищем игрока… {0} сек.\nЕсли соперник не найдётся, сервер подберёт бота.";
        public string Connecting = "Подключаемся к серверу…";
        public string Cancel = "Отмена";
        public string Retry = "Повторить";
        public string Close = "Закрыть";
        public string ErrorTitle = "Нет связи с сервером";
        public string ErrorBody = "Не удалось получить ответ. Повторите запрос: существующий поиск или матч сохранится на сервере.";
        public string UnsupportedLoadoutTitle = "Отряд пока не поддерживается";
        [TextArea] public string UnsupportedLoadoutBody = "Текущая серверная сборка поддерживает один приказ усиления брони или пустые слоты приказов. Измените состав отряда и повторите поиск.";
        public string NoServer = "Запустите локальный сервер через run-matchmaking.ps1. Для телефона оставьте USB подключённым.";
        public void Validate()
        {
            if (string.IsNullOrEmpty(ContentVersion) || string.IsNullOrEmpty(MatchSceneName) || PollMilliseconds < 500 || PollMilliseconds > 5000)
                throw new System.InvalidOperationException("Network queue settings are incomplete.");
        }
    }
}
