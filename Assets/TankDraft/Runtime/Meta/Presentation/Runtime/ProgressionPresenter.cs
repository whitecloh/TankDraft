using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TankDraft.Application;
using TankDraft.Content;
using TankDraft.Contracts;
using UnityEngine;

namespace TankDraft.UI
{
    // This presenter is a thin client for authoritative progression. It has no balance
    // calculation and persists an unresolved intent before the first network attempt.
    public sealed class ProgressionPresenter : IDisposable
    {
        [Serializable] private sealed class PendingIntent
        {
            public string OperationId;
            public string Kind;
            public string TargetId;
            public long ExpectedSequence;
        }

        private readonly IServerProgressionClient client;
        private readonly ProfileService profiles;
        private readonly MetaCatalogAsset content;
        private readonly UIProgressionWindow window;
        private readonly ProgressionUiRules rules;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly string journalPath;
        private ProgressionSnapshot snapshot;
        private string selectedUnit;
        private bool shop, busy, disposed;

        public ProgressionPresenter(IServerProgressionClient client, ProfileService profiles, MetaCatalogAsset content, UIProgressionWindow window, string journalScope = null)
        {
            this.client = client;
            this.profiles = profiles;
            this.content = content;
            this.window = window;
            if (client != null && window != null)
            {
                rules = ProgressionUiRules.Load(content.ProgressionRules);
                if (IsValidJournalScope(journalScope))
                    journalPath = Path.Combine(UnityEngine.Application.persistentDataPath, "TankDraft", "Progression", journalScope, "intent-v1.json");
            }
        }

        public bool IsAvailable => client != null && window != null && rules != null && journalPath != null;
        public bool CanPresentResult => window != null;
        public bool IsBusy => busy;

        public void OpenShop()
        {
            if (!EnsureAvailable()) return;
            shop = true;
            selectedUnit = null;
            _ = OpenAsync();
        }

        public void OpenUnit(string unitId)
        {
            if (!EnsureAvailable()) return;
            if (string.IsNullOrWhiteSpace(unitId) || !rules.TryGetUnit(unitId, out _)) return;
            shop = false;
            selectedUnit = unitId;
            _ = OpenAsync();
        }

        // Result values were already written by the authoritative settlement. This is
        // presentation only: no local XP, coins, or fragments are added here.
        public bool ShowResult(RecentMatchResult result, Action completed, Action dismissed = null)
        {
            if (window == null || result == null || result.ParticipantUnitIds.Count != 4) return false;
            var cards = new ProgressionCardModel[4];
            for (int index = 0; index < cards.Length; index++)
            {
                string id = result.ParticipantUnitIds[index];
                ContentEntryAsset visual = content.GetVisual(id);
                cards[index] = new ProgressionCardModel(visual.Title, "Мастерство", visual.Icon,
                    "+" + result.MasteryXpPerUnit + " XP", result.MasteryApplied ? "Получено" : "Ожидает выдачи");
            }
            window.Bind(result.Won ? "ПОБЕДА" : "БОЙ ЗАВЕРШЁН", "Опыт распределён между техникой, участвовавшей в бою.", cards,
                new[] { new ProgressionActionModel("ПРОДОЛЖИТЬ", true, () => ShowCoinResult(result, completed, dismissed)) }, () => { Close(); dismissed?.Invoke(); });
            return true;
        }

        private void ShowCoinResult(RecentMatchResult result, Action completed, Action dismissed)
        {
            string body = result.State == RewardDeliveryState.Applied
                ? "Получено: " + result.Amount + " " + result.Currency + "."
                : result.State == RewardDeliveryState.NeedsReview
                    ? "Награда находится на проверке."
                    : "Награда ещё обрабатывается.";
            window.Bind("НАГРАДА", body, Array.Empty<ProgressionCardModel>(),
                new[] { new ProgressionActionModel("В МЕНЮ", true, () => { Close(); completed?.Invoke(); }) }, () => { Close(); dismissed?.Invoke(); });
        }

        private bool EnsureAvailable()
        {
            if (IsAvailable) return true;
            window?.Bind("Прокачка", "Серверная прокачка пока не настроена для этого окружения.", Array.Empty<ProgressionCardModel>(), Array.Empty<ProgressionActionModel>(), Close);
            return false;
        }

        private async Task OpenAsync()
        {
            if (disposed || busy) return;
            bool loaded = false;
            try
            {
                busy = true;
                ShowLoading();
                snapshot = await client.GetAsync(stop.Token);
                ValidateSnapshot(snapshot);
                PendingIntent pending = ReadPending();
                if (pending != null)
                {
                    snapshot = await client.ExecuteAsync(pending.Kind, pending.TargetId, ParseGuid(pending.OperationId), pending.ExpectedSequence, stop.Token);
                    ValidateSnapshot(snapshot);
                    if (snapshot.OperationStatus == "Completed") ClearPending();
                }
                loaded = true;
            }
            catch (OperationCanceledException) when (disposed) { }
            catch (ProgressionRejectedException rejection)
            {
                if (!disposed) await HandleRejectedAsync(rejection);
            }
            catch (Exception error)
            {
                if (!disposed) ShowFailure(error);
            }
            finally
            {
                busy = false;
                if (loaded && !disposed) Render();
            }
        }

        private void ShowLoading() => window.Bind("Прокачка", "Загрузка…", Array.Empty<ProgressionCardModel>(), Array.Empty<ProgressionActionModel>(), Close);

        private void ShowFailure(Exception error)
        {
            string body = error is InvalidDataException && error.Message == "Fusion progression is disabled."
                ? "Серверная прокачка отключена для этого тестового окружения."
                : "Результат запроса пока не подтверждён. Повторим проверку без новой покупки.";
            window.Bind("Прокачка", body, Array.Empty<ProgressionCardModel>(), new[]
            {
                new ProgressionActionModel("ПОВТОРИТЬ", true, () => _ = OpenAsync())
            }, Close);
        }

        private void Render()
        {
            if (shop) RenderShop(); else RenderUnit();
        }

        private void RenderShop()
        {
            var cards = new List<ProgressionCardModel>();
            var actions = new List<ProgressionActionModel>();
            foreach (ProgressionUiRules.Offer offer in rules.Offers)
            {
                if (cards.Count == 4) break;
                Sprite icon = null;
                string title;
                if (!string.IsNullOrEmpty(offer.UnitId))
                {
                    ContentEntryAsset visual = content.GetVisual(offer.UnitId);
                    icon = visual.Icon;
                    title = visual.Title;
                }
                else title = "Серебряный пакет";
                string amount = offer.Bits > 0 ? "+" + offer.Bits + " фрагм." : "3 карты";
                cards.Add(new ProgressionCardModel(title, amount, icon, offer.Price + " " + offer.Currency, string.Empty));
                actions.Add(new ProgressionActionModel(title + " · " + offer.Price + " " + offer.Currency, CanAct, () => BeginIntent("BuyOffer", offer.Sku)));
            }
            window.Bind("Магазин", PendingBody("Монеты: " + profiles.Current.Coins + ". Кристаллы: " + profiles.Current.Gems + ".\nФрагменты для улучшения техники и наборы карт."), cards.ToArray(), actions.ToArray(), Close);
        }

        private void RenderUnit()
        {
            UnitProgressSnapshot progress = FindUnit(selectedUnit);
            ContentEntryAsset visual = content.GetVisual(selectedUnit);
            var cards = new[]
            {
                new ProgressionCardModel(visual.Title, "Уровень " + progress.Level, visual.Icon, "Фрагменты: " + progress.Bits, "Мастерство: " + progress.MasteryXp + " XP")
            };
            var actions = new List<ProgressionActionModel>();
            if (rules.TryGetLevel(progress.Level, out ProgressionUiRules.Level level))
                actions.Add(new ProgressionActionModel("УЛУЧШИТЬ · " + level.BitsCost + " фрагм. · " + level.CoinsCost + " CO", CanAct, () => BeginIntent("UpgradeUnit", selectedUnit)));
            actions.Add(new ProgressionActionModel("ДОБАВИТЬ МАСТЕРСТВО · " + rules.BoostChipCost + " чип", CanAct, () => BeginIntent("BoostMastery", selectedUnit)));
            if (rules.TryGetNextMastery(progress.MasteryXp, out ProgressionUiRules.Mastery next) && rules.TryGetMasteryCost(next.Level, out int masteryPrice))
                actions.Add(new ProgressionActionModel("КУПИТЬ УРОВЕНЬ МАСТЕРСТВА " + next.Level + " · " + masteryPrice + " GM", CanAct, () => BeginIntent("BuyMasteryLevel", selectedUnit)));
            if (rules.TryGetClaimable(selectedUnit, progress.MasteryXp, snapshot.ClaimedMilestones, out ProgressionUiRules.Mastery claim))
                actions.Add(new ProgressionActionModel("ПОЛУЧИТЬ НАГРАДУ МАСТЕРСТВА", CanAct, () => BeginIntent("ClaimMilestone", selectedUnit + ":" + claim.Level)));
            window.Bind("Прокачка", PendingBody("Чипы мастерства: " + snapshot.MasteryChips + ". Монеты: " + profiles.Current.Coins + ". Кристаллы: " + profiles.Current.Gems + "."), cards, actions.ToArray(), Close);
        }

        private async void BeginIntent(string kind, string targetId)
        {
            if (busy || disposed || snapshot == null) return;
            bool completed = false;
            bool revealPack = kind == "BuyOffer" && rules.TryGetOffer(targetId, out ProgressionUiRules.Offer offer) && !string.IsNullOrEmpty(offer.PackId);
            try
            {
                PendingIntent pending = ReadPending();
                if (pending == null)
                {
                    pending = new PendingIntent { OperationId = Guid.NewGuid().ToString("N"), Kind = kind, TargetId = targetId, ExpectedSequence = snapshot.Sequence };
                    WritePending(pending);
                }
                else if (pending.Kind != kind || pending.TargetId != targetId)
                {
                    ShowFailure(new InvalidOperationException("A previous progression request is unresolved."));
                    return;
                }

                busy = true;
                Render();
                snapshot = await client.ExecuteAsync(pending.Kind, pending.TargetId, ParseGuid(pending.OperationId), pending.ExpectedSequence, stop.Token);
                ValidateSnapshot(snapshot);
                if (snapshot.OperationStatus == "Completed")
                {
                    ClearPending();
                    await RefreshProfileAsync();
                    if (revealPack && snapshot.ResolvedUnitIds.Count > 0)
                    {
                        ShowPackReveal(snapshot.ResolvedUnitIds);
                        return;
                    }
                }
                completed = true;
            }
            catch (OperationCanceledException) when (disposed) { }
            catch (ProgressionRejectedException rejection)
            {
                if (!disposed) await HandleRejectedAsync(rejection);
            }
            catch (Exception error)
            {
                if (!disposed) ShowFailure(error);
            }
            finally
            {
                busy = false;
                if (completed && !disposed) Render();
            }
        }

        private UnitProgressSnapshot FindUnit(string id)
        {
            foreach (UnitProgressSnapshot item in snapshot.Units) if (item.Id == id) return item;
            return new UnitProgressSnapshot(id, 1, 0, 0);
        }

        private void ShowPackReveal(IReadOnlyList<string> resolvedUnitIds)
        {
            var cards = new List<ProgressionCardModel>();
            for (int index = 0; index < resolvedUnitIds.Count && cards.Count < 4; index++)
            {
                string id = resolvedUnitIds[index];
                ContentEntryAsset visual = content.GetVisual(id);
                UnitProgressSnapshot progress = FindUnit(id);
                cards.Add(new ProgressionCardModel(visual.Title, "Фрагменты получены", visual.Icon, "Всего: " + progress.Bits + " фрагм.", "Получено"));
            }
            window.Bind("ПАК ОТКРЫТ", "Ваши карты. Показано количество фрагментов в коллекции.", cards.ToArray(),
                new[] { new ProgressionActionModel("В МЕНЮ", true, Close) }, Close);
        }

        private bool CanAct => !busy && !HasUnresolvedOperation;
        private bool HasUnresolvedOperation => snapshot != null && !string.IsNullOrEmpty(snapshot.OperationStatus) && snapshot.OperationStatus != "Completed";
        private string PendingBody(string body) => HasUnresolvedOperation ? body + "\n\nПредыдущее действие ожидает проверки сервера. Новые покупки временно недоступны." : body;
        private async Task RefreshProfileAsync()
        {
            if (profiles.IsServerBacked) await profiles.RefreshAsync(stop.Token);
        }

        private async Task HandleRejectedAsync(ProgressionRejectedException rejection)
        {
            // These are explicitly pre-journal validation outcomes. Other errors keep
            // the journal so the same idempotency key can be replayed safely.
            ClearPending();
            snapshot = await client.GetAsync(stop.Token);
            ValidateSnapshot(snapshot);
            await RefreshProfileAsync();
            window.Bind("Прокачка", RejectionText(rejection.Code), Array.Empty<ProgressionCardModel>(), new[]
            {
                new ProgressionActionModel("ОБНОВИТЬ", true, () => _ = OpenAsync())
            }, Close);
        }

        private static string RejectionText(string code)
        {
            switch (code)
            {
                case "insufficient_funds": return "Недостаточно монет или кристаллов для этого действия.";
                case "insufficient_bits": return "Недостаточно фрагментов техники.";
                case "insufficient_chips": return "Недостаточно чипов мастерства.";
                case "stale_progression_sequence": return "Прогресс был изменён на сервере. Состояние обновлено.";
                case "unit_max_level": return "Техника достигла максимального уровня.";
                case "mastery_max_level": return "Мастерство техники достигло максимального уровня.";
                case "unit_unavailable": return "Эта техника пока недоступна.";
                case "milestone_unavailable": return "Награда мастерства пока недоступна.";
                default: return "Это действие больше недоступно. Серверное состояние обновлено.";
            }
        }

        private void ValidateSnapshot(ProgressionSnapshot value)
        {
            if (value == null || value.RulesVersion != rules.Version) throw new InvalidDataException("Progression balance version does not match the client content.");
        }

        private PendingIntent ReadPending()
        {
            if (!File.Exists(journalPath)) return null;
            PendingIntent value = JsonUtility.FromJson<PendingIntent>(File.ReadAllText(journalPath));
            if (value == null || !Guid.TryParseExact(value.OperationId, "N", out _) || !IsKind(value.Kind) || !ProgressionSnapshot.IsValidId(value.TargetId) || value.ExpectedSequence < 0)
                throw new InvalidDataException("Progression intent journal is invalid.");
            return value;
        }

        private void WritePending(PendingIntent value)
        {
            if (journalPath == null) throw new InvalidOperationException("Progression account scope is unavailable.");
            string directory = Path.GetDirectoryName(journalPath);
            Directory.CreateDirectory(directory);
            string temporary = journalPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value));
            if (File.Exists(journalPath)) File.Replace(temporary, journalPath, null);
            else File.Move(temporary, journalPath);
        }

        private void ClearPending()
        {
            if (File.Exists(journalPath)) File.Delete(journalPath);
        }

        private static Guid ParseGuid(string value) => Guid.ParseExact(value, "N");
        private static bool IsKind(string value) => value == "UpgradeUnit" || value == "BuyOffer" || value == "BoostMastery" || value == "BuyMasteryLevel" || value == "ClaimMilestone";
        public static bool IsValidJournalScope(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private void Close()
        {
            // A running operation owns the selected view state until it has either
            // received a response or written a durable unresolved intent.
            if (busy) return;
            selectedUnit = null;
            shop = false;
            window?.Hide();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stop.Cancel();
            if (window != null) window.Release();
        }
    }
}
