using System.Text.Json;
using System;
using System.Text.Json.Serialization;
using System.Linq;

namespace Amazon.Lambda.Logging.AspNetCore.JsonConverters
{
    internal class JsonExceptionConverter : JsonConverter<Exception>
    {
        public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
        {
            // Not every property on exceptions are serialiable, skip those who are know to be bad
            var serializableProperties = value.GetType()
                .GetProperties()
                .Select(prop => new { prop.Name, Value = prop.GetValue(value) })
                .Where(prop => prop.Name != nameof(Exception.TargetSite));

            writer.WriteStartObject();

            foreach (var prop in serializableProperties)
            {
                writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name);
                JsonSerializer.Serialize(writer, prop.Value, options);
            }

            writer.WriteEndObject();
        }
    }
}