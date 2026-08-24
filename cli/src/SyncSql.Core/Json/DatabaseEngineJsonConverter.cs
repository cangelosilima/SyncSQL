using System.Text.Json;
using System.Text.Json.Serialization;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Json;

/// <summary>Serializes <see cref="DatabaseEngine"/> as the lowercase "mssql"/"oracle" tokens config/servers.json and the extracted object "-- Engine:" header use.</summary>
public sealed class DatabaseEngineJsonConverter : JsonConverter<DatabaseEngine>
{
    public override DatabaseEngine Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (!DatabaseEngineNames.TryParse(value, out DatabaseEngine engine))
        {
            throw new JsonException($"Unknown database engine '{value}' - expected '{DatabaseEngineNames.MsSql}' or '{DatabaseEngineNames.Oracle}'.");
        }

        return engine;
    }

    public override void Write(Utf8JsonWriter writer, DatabaseEngine value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToConfigString());
}
