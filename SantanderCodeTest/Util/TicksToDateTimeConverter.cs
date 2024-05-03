namespace SantanderCodeTest.Util;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class TicksToDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {

        var dateTime = new DateTime(value.Ticks);
        writer.WriteStringValue(dateTime.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
