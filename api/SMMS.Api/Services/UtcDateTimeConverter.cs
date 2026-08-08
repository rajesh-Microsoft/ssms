using System.Text.Json;
using System.Text.Json.Serialization;

namespace SMMS.Api.Services;

/// <summary>
/// Every timestamp in this app is written as UTC, but SQL Server hands datetime2 back with an
/// unspecified kind. Without this the JSON would carry no "Z" and every browser would read a
/// UTC instant as if it were local time — the gate log showed 08:41 for a parcel taken at 14:11 IST.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        writer.WriteStringValue(utc);
    }
}
