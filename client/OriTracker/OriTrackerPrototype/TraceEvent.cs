using System;
using Newtonsoft.Json;

namespace OriTracker
{
    public class StringTimestampConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var timestamp = (double)value;
            writer.WriteValue(string.Format("{0:0.}", timestamp));

        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(double);
        }
    }

    public class TraceEvent
    {
        public double X { get; set; }

        public double Y { get; set; }

        [JsonConverter(typeof(StringTimestampConverter))]
        public double Timestamp { get; set; }

        // Whether this is the beginning of a new line (say, we just entered from menu)
        public bool Start { get; internal set; }
    }

}
