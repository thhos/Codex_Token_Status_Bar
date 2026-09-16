using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexPetCredits {
    internal static class Json {
        public static JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(object value, string key) { object result; return Map(value).TryGetValue(key, out result) ? result : null; }
        public static string Text(object value, string key, string fallback = "") { return Get(value, key) == null ? fallback : Convert.ToString(Get(value, key)); }
        public static double Number(object value, string key, double fallback = 0) { try { var v = Get(value, key); return v == null ? fallback : Convert.ToDouble(v); } catch { return fallback; } }
        public static bool Flag(object value, string key) { return Get(value, key) is bool && (bool)Get(value, key); }
        public static IEnumerable<object> Items(object value) { return (value as IEnumerable ?? new object[0]).Cast<object>(); }
    }

}
