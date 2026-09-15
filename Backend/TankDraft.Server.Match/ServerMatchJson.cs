using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TankDraft.Contracts.Battle;

namespace TankDraft.Server.Match;

public static class ServerMatchJson
{
    public static void Register(JsonSerializerOptions options)
    {
        options.Converters.Add(new BattleValueConverter<BattleVec>());
        options.Converters.Add(new BattleValueConverter<BattleEntityState>());
        options.Converters.Add(new BattleValueConverter<BattleEvent>());
        options.Converters.Add(new BattleValueConverter<BattleResolutionHit>());
    }

    // Existing shared immutable structs intentionally carry no transport-specific attributes.
    // Explicit construction prevents System.Text.Json from silently producing default readonly fields.
    private sealed class BattleValueConverter<T> : JsonConverter<T> where T : struct
    {
        private static readonly FieldInfo[] Fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
        private static readonly ConstructorInfo Constructor = typeof(T).GetConstructors().Single();
        private static readonly FieldInfo[] Parameters = Constructor.GetParameters().Select(parameter =>
            Fields.Single(field => string.Equals(field.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))).ToArray();

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Battle value must be an object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name) || !Fields.Any(field => field.Name == property.Name))
                    throw new JsonException("Unknown or duplicate battle value field.");
            if (names.Count != Fields.Length) throw new JsonException("Missing battle value field.");
            var values = Parameters.Select(field => root.GetProperty(field.Name).Deserialize(field.FieldType, options)).ToArray();
            try { return (T)Constructor.Invoke(values); }
            catch (TargetInvocationException) { throw new JsonException("Invalid battle value."); }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var field in Fields)
            {
                writer.WritePropertyName(field.Name);
                JsonSerializer.Serialize(writer, field.GetValue(value), field.FieldType, options);
            }
            writer.WriteEndObject();
        }
    }
}
