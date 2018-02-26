using System;
using Google.Cloud.Firestore;
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

    [FirestoreData]
    public class TraceEvent
    {
        [FirestoreProperty]
        public double X { get; set; }

        [FirestoreProperty]
        public double Y { get; set; }

        [JsonConverter(typeof(StringTimestampConverter))]
        public double Timestamp { get; set; }
    }

}
