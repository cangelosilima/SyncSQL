using System.Text.Json;
using System.Text.Json.Serialization;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Json;

/// <summary>Serializes <see cref="GrantState"/> as the literal "GRANT"/"DENY" strings catalog.json (and site/src/types.ts) expect, not the default enum member name.</summary>
public sealed class GrantStateJsonConverter : JsonConverter<GrantState>
{
    public override GrantState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        return value?.ToUpperInvariant() switch
        {
            "GRANT" => GrantState.Grant,
            "DENY" => GrantState.Deny,
            _ => throw new JsonException($"Unknown grant state '{value}' - expected 'GRANT' or 'DENY'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, GrantState value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value == GrantState.Deny ? "DENY" : "GRANT");
    }
}
