// A small stand-in for UnityEngine.JsonUtility: public fields and [SerializeField] fields of
// [Serializable] classes / structs, arrays, List<T>, enums as ints. Like Unity, null arrays / lists /
// strings are written as empty, and fields missing from the JSON keep their initialised value.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Harness
{
    public static class Json
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static IEnumerable<FieldInfo> SerializedFields(Type t)
        {
            foreach (FieldInfo f in t.GetFields(Fields))
            {
                if (f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) continue;
                if (f.IsPublic || f.GetCustomAttribute<UnityEngine.SerializeField>() != null) yield return f;
            }
        }

        public static string ToJson(object o)
        {
            var sb = new StringBuilder();
            WriteObject(sb, o);
            return sb.ToString();
        }

        private static void WriteObject(StringBuilder sb, object o)
        {
            sb.Append('{');
            bool first = true;
            foreach (FieldInfo f in SerializedFields(o.GetType()))
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(f.Name).Append("\":");
                WriteValue(sb, f.FieldType, f.GetValue(o));
            }
            sb.Append('}');
        }

        private static void WriteValue(StringBuilder sb, Type t, object v)
        {
            if (t == typeof(string)) { WriteString(sb, (string)v ?? ""); return; }
            if (t == typeof(bool)) { sb.Append((bool)v ? "true" : "false"); return; }
            if (t == typeof(float)) { sb.Append(((float)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (t == typeof(double)) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (t.IsEnum) { sb.Append(System.Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            if (t.IsPrimitive) { sb.Append(System.Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            Type element = ElementType(t);
            if (element != null)
            {
                sb.Append('[');
                if (v != null)
                {
                    bool first = true;
                    foreach (object item in (IEnumerable)v)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, element, item);
                    }
                }
                sb.Append(']');
                return;
            }
            if (v == null) v = Activator.CreateInstance(t, true);
            WriteObject(sb, v);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static Type ElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];
            return null;
        }

        public static T FromJson<T>(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("JSON parse error: The document is empty.");
            int i = 0;
            object tree = Parse(s, ref i);
            SkipWs(s, ref i);
            if (i != s.Length) throw new ArgumentException("JSON parse error: The document root must not follow by other values.");
            if (!(tree is Dictionary<string, object> dict)) throw new ArgumentException("JSON must represent an object type.");
            object target = Activator.CreateInstance(typeof(T), true);
            Fill(target, dict);
            return (T)target;
        }

        private static void Fill(object target, Dictionary<string, object> dict)
        {
            foreach (FieldInfo f in SerializedFields(target.GetType()))
            {
                if (!dict.TryGetValue(f.Name, out object raw)) continue;
                f.SetValue(target, ConvertTo(f.FieldType, raw, f.GetValue(target)));
            }
        }

        private static object ConvertTo(Type t, object raw, object existing)
        {
            if (t == typeof(string)) return raw as string ?? "";
            if (t == typeof(bool)) return raw is bool b ? b : raw is double d0 && d0 != 0;
            if (t.IsEnum) return Enum.ToObject(t, (long)ToDouble(raw));
            if (t.IsPrimitive) return System.Convert.ChangeType(ToDouble(raw), t, CultureInfo.InvariantCulture);
            Type element = ElementType(t);
            if (element != null)
            {
                var items = raw as List<object> ?? new List<object>();
                if (t.IsArray)
                {
                    Array array = Array.CreateInstance(element, items.Count);
                    for (int k = 0; k < items.Count; k++) array.SetValue(ConvertTo(element, items[k], null), k);
                    return array;
                }
                var list = (IList)Activator.CreateInstance(t);
                foreach (object item in items) list.Add(ConvertTo(element, item, null));
                return list;
            }
            object value = existing ?? Activator.CreateInstance(t, true);
            if (raw is Dictionary<string, object> dict) Fill(value, dict);
            return value;
        }

        private static double ToDouble(object raw)
        {
            if (raw is double d) return d;
            if (raw is bool b) return b ? 1 : 0;
            return 0;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object Parse(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new ArgumentException("JSON parse error: unexpected end.");
            char c = s[i];
            if (c == '{')
            {
                var dict = new Dictionary<string, object>();
                i++;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return dict; }
                while (true)
                {
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new ArgumentException("JSON parse error: expected a key.");
                    string key = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new ArgumentException("JSON parse error: expected ':'.");
                    i++;
                    dict[key] = Parse(s, ref i);
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return dict; }
                    throw new ArgumentException("JSON parse error: expected ',' or '}'.");
                }
            }
            if (c == '[')
            {
                var list = new List<object>();
                i++;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(Parse(s, ref i));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return list; }
                    throw new ArgumentException("JSON parse error: expected ',' or ']'.");
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new ArgumentException("JSON parse error: Invalid value.");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append((char)System.Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(s[i]);
                i++;
            }
            if (i >= s.Length) throw new ArgumentException("JSON parse error: unterminated string.");
            i++;
            return sb.ToString();
        }
    }
}
