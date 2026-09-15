using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Local intent routing only. The authenticated server must supply the assignment again.
    // No local record grants a seat, decides a result or contains authentication tokens.
    public sealed class FusionResumeStorage
    {
        readonly string root, account, instance, version;
        public FusionResumeStorage(string root, string account, string instance, string version)
        {
            if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root) || string.IsNullOrEmpty(account) || account.Length > 64 ||
                string.IsNullOrEmpty(instance) || instance.Length > 80 || version?.Length != 64) throw new ArgumentException("Invalid resume scope.");
            this.root = Path.GetFullPath(root); this.account = account; this.instance = instance; this.version = version;
        }
        public string MatchKey(string match, int side)
        {
            if (string.IsNullOrEmpty(match) || match.Length > 128 || side < 0 || side > 1) throw new ArgumentException("Invalid resume assignment.");
            var binding = new JArray("fusion-resume-v1", account, instance, version, match, side).ToString(Formatting.None);
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(binding))).Replace("-", "").ToLowerInvariant();
        }
        public string JournalPath(string match, int side) => Path.Combine(root, MatchKey(match, side), "intent.json");
        public string ProgressionJournalScope
        {
            get
            {
                // Stable across server restarts/content revisions: unresolved purchases must keep their id.
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("TankDraft/B16D9/progression-v1/" + account))).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
