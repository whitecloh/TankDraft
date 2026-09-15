using System;
using TankDraft.Match.Content;
using UnityEngine;

namespace TankDraft.Match.ServerClient
{
    [CreateAssetMenu(menuName = "TankDraft/Match/Server Client")]
    public sealed class ServerClientSettings : ScriptableObject
    {
        public MatchSettingsAsset MatchSettings;
        public string ContentVersion;
        public string Protocol = "tankdraft-server-v2";
        public int PollMilliseconds = 100, RetryMilliseconds = 1000, TimeoutMilliseconds = 5000, MaxResponseBytes = 1048576, MaxBufferedFrames = 8;
        public float PresentationDelaySeconds = .2f;
        public float MaxPresentationDelaySeconds = .8f;
        public int TargetFrameRate = 60;
        public string BotOpponentLabel = "Бот";
        public string Connecting = "Подключение к серверу", Recovering = "Восстанавливаем связь…", Waiting = "Ожидаем сервер", Failed = "Соединение недоступно", Title = "Сетевой бой", TimerFormat = "Выбор: {0} сек.";
        public void Validate()
        {
            if (!MatchSettings || ContentVersion == null || ContentVersion.Length != 64 || Protocol != "tankdraft-server-v2" ||
                PollMilliseconds < 100 || PollMilliseconds > 1000 || RetryMilliseconds < 250 || RetryMilliseconds > 10000 ||
                TimeoutMilliseconds < 1000 || TimeoutMilliseconds > 15000 || MaxResponseBytes < 8192 || MaxResponseBytes > 1048576 ||
                TargetFrameRate < 30 || TargetFrameRate > 120 || MaxBufferedFrames < 2 || MaxBufferedFrames > 16 || PresentationDelaySeconds < .1f || PresentationDelaySeconds > 1f || MaxPresentationDelaySeconds < PresentationDelaySeconds || MaxPresentationDelaySeconds > 1f)
                throw new InvalidOperationException("Invalid authored server client settings.");
            MatchSettings.Validate();
        }
    }
}
