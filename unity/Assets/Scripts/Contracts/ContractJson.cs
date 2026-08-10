// Central JSON settings for every V19 contract (de)serialization on device.
//
// Why this exists: Newtonsoft's default DateParseHandling silently converts any
// JSON string that LOOKS like a date into a DateTime while reading — even when
// the target property is a string. The value then round-trips through
// DateTime.ToString() with the CURRENT CULTURE ("07/04/2026 12:00:00"),
// corrupting the ISO-8601 timestamps our PC services expect byte-for-byte
// (caught by ContractSerializationTests on the first real Unity run).
//
// Rule: production transport/UI code serializes contracts through ContractJson,
// never through bare JsonConvert. The static ctor also installs these settings
// as JsonConvert.DefaultSettings so any stray call after first touch stays safe.

using System.Globalization;
using Newtonsoft.Json;

namespace MLOmega.Contracts.V19
{
    public static class ContractJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // Timestamps are contract STRINGS — never auto-parse them.
            DateParseHandling = DateParseHandling.None,
            // If a real DateTime ever appears, keep it ISO and culture-free.
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Ignore
        };

        static ContractJson()
        {
            JsonConvert.DefaultSettings = () => Settings;
        }

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>
        /// Culture-safe replacement for <c>JObject.Parse</c>: that API uses its own
        /// reader whose default DateParseHandling converts ISO timestamp strings to
        /// DateTime tokens (then culture-formats them on string reads).
        /// </summary>
        public static Newtonsoft.Json.Linq.JObject ParseObject(string json)
        {
            using (var reader = new JsonTextReader(new System.IO.StringReader(json))
                   { DateParseHandling = DateParseHandling.None })
            {
                return Newtonsoft.Json.Linq.JObject.Load(reader);
            }
        }
    }
}
