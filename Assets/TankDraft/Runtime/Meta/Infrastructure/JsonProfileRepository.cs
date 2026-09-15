using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using UnityEngine;

namespace TankDraft.Infrastructure
{
    public sealed class JsonProfileRepository : IProfileRepository
    {
        private const int CurrentVersion = 1;
        private const long MaximumFileLength = 1024L * 1024L;
        private readonly string path;

        public JsonProfileRepository(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A profile path is required.", nameof(path));
            this.path = path;
        }

        public ProfileSnapshot Load()
        {
            if (!File.Exists(path)) return null;

            FileInfo info = new FileInfo(path);
            if (info.Length > MaximumFileLength) throw new InvalidDataException("Profile file exceeds the maximum size.");

            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                throw;
            }

            ProfileDto dto;
            try
            {
                using (StringReader text = new StringReader(json))
                using (JsonTextReader reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    JObject root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read()) throw new InvalidDataException("Profile JSON contains trailing content.");
                    ValidateSchema(root);
                }
                dto = JsonUtility.FromJson<ProfileDto>(json);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Profile JSON is invalid.", exception);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Profile JSON is invalid.", exception);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("Profile JSON is invalid.", exception);
            }

            if (dto == null || dto.version != CurrentVersion) throw new InvalidDataException("Profile schema version is unsupported.");
            try
            {
                return new ProfileSnapshot(dto.playerName, dto.commanderLevel, dto.arenaLevel, dto.arenaProgress, dto.energy, dto.gems, dto.coins, dto.mastery, dto.ownedIds, dto.unitIds, dto.orderIds);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Profile payload is invalid.", exception);
            }
        }

        public void Save(ProfileSnapshot profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ProfileDto dto = new ProfileDto();
                dto.version = CurrentVersion;
                dto.playerName = profile.PlayerName;
                dto.commanderLevel = profile.CommanderLevel;
                dto.arenaLevel = profile.ArenaLevel;
                dto.arenaProgress = profile.ArenaProgress;
                dto.energy = profile.Energy;
                dto.gems = profile.Gems;
                dto.coins = profile.Coins;
                dto.mastery = profile.Mastery;
                dto.ownedIds = Copy(profile.OwnedIds);
                dto.unitIds = Copy(profile.UnitIds);
                dto.orderIds = Copy(profile.OrderIds);
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(dto));

                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path)) File.Replace(temporaryPath, path, path + ".bak");
                else File.Move(temporaryPath, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A failed cleanup must not hide the result of the atomic replacement.
                }
                catch (UnauthorizedAccessException)
                {
                    // A failed cleanup must not hide the result of the atomic replacement.
                }
            }
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = values[index];
            return copy;
        }

        // JsonUtility serializes the DTO; Newtonsoft validates the untrusted on-disk schema before it is deserialized.
        private static void ValidateSchema(JObject root)
        {
            HashSet<string> expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "version", "playerName", "commanderLevel", "arenaLevel", "arenaProgress", "energy", "gems", "coins", "mastery", "ownedIds", "unitIds", "orderIds"
            };
            if (root.Count != expected.Count) throw new InvalidDataException("Profile schema is incomplete or contains extra fields.");
            foreach (JProperty property in root.Properties())
            {
                if (!expected.Remove(property.Name)) throw new InvalidDataException("Profile schema contains an invalid field.");
            }
            if (expected.Count != 0) throw new InvalidDataException("Profile schema is incomplete.");

            RequireInteger(root, "version");
            RequireInteger(root, "commanderLevel");
            RequireInteger(root, "arenaLevel");
            RequireInteger(root, "arenaProgress");
            RequireInteger(root, "energy");
            RequireInteger(root, "gems");
            RequireInteger(root, "coins");
            RequireInteger(root, "mastery");
            RequireString(root, "playerName");
            RequireStringArray(root, "ownedIds");
            RequireStringArray(root, "unitIds");
            RequireStringArray(root, "orderIds");
        }

        private static void RequireInteger(JObject root, string name)
        {
            JToken token = root[name];
            if (token == null || token.Type != JTokenType.Integer) throw new InvalidDataException("Profile field must be an integer: " + name);
            try
            {
                token.Value<int>();
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("Profile integer is out of range: " + name, exception);
            }
        }

        private static void RequireString(JObject root, string name)
        {
            JToken token = root[name];
            if (token == null || token.Type != JTokenType.String) throw new InvalidDataException("Profile field must be a string: " + name);
        }

        private static void RequireStringArray(JObject root, string name)
        {
            JArray values = root[name] as JArray;
            if (values == null) throw new InvalidDataException("Profile field must be an array: " + name);
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index].Type != JTokenType.String) throw new InvalidDataException("Profile id arrays must contain strings: " + name);
            }
        }

        [Serializable]
        private sealed class ProfileDto
        {
            public int version;
            public string playerName;
            public int commanderLevel;
            public int arenaLevel;
            public int arenaProgress;
            public int energy;
            public int gems;
            public int coins;
            public int mastery;
            public string[] ownedIds;
            public string[] unitIds;
            public string[] orderIds;
        }
    }
}
