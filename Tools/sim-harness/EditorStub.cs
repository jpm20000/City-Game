// Stand-ins for the UnityEditor APIs the asset-reading EditMode tests use. Assets are read from their
// Unity YAML: types compiled into this harness (the Simulation asmdef) are filled by reflection; others
// (Assembly-CSharp types such as BuildingDefinition, AgeVisualSet) become YamlAsset, readable through
// SerializedObject.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Harness
{
    public sealed class YamlAsset : ScriptableObject
    {
        public string ClassName;
        public Dictionary<string, object> Data;
    }

    public static class Yaml
    {
        // Parses a Unity YAML asset's first document body (the mapping under "MonoBehaviour:").
        public static Dictionary<string, object> ParseAsset(string path)
        {
            var lines = new List<string>();
            bool inDoc = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                if (raw.StartsWith("--- ")) { if (inDoc) break; inDoc = true; continue; }
                if (!inDoc || raw.StartsWith("%")) continue;
                if (raw.Trim().Length == 0) continue;
                lines.Add(raw);
            }
            int i = 0;
            var root = (Dictionary<string, object>)ParseBlock(lines, ref i, 0);
            return (Dictionary<string, object>)root.Values.First();
        }

        private static int Indent(string s) { int n = 0; while (n < s.Length && s[n] == ' ') n++; return n; }

        private static object ParseBlock(List<string> lines, ref int i, int indent)
        {
            if (i < lines.Count && Indent(lines[i]) == indent && lines[i].TrimStart().StartsWith("- "))
                return ParseSeq(lines, ref i, indent);
            if (i < lines.Count && Indent(lines[i]) == indent && lines[i].Trim() == "-")
                return ParseSeq(lines, ref i, indent);
            var map = new Dictionary<string, object>();
            while (i < lines.Count && Indent(lines[i]) == indent && !lines[i].TrimStart().StartsWith("-"))
            {
                string line = lines[i].Substring(indent);
                int colon = line.IndexOf(':');
                string key = line.Substring(0, colon);
                string rest = line.Substring(colon + 1).Trim();
                i++;
                if (rest.Length > 0) map[key] = Scalar(rest);
                else if (i < lines.Count && (Indent(lines[i]) > indent || (Indent(lines[i]) == indent && lines[i].TrimStart().StartsWith("-"))))
                    map[key] = ParseBlock(lines, ref i, Indent(lines[i]));
                else map[key] = "";
            }
            return map;
        }

        private static List<object> ParseSeq(List<string> lines, ref int i, int indent)
        {
            var list = new List<object>();
            while (i < lines.Count && Indent(lines[i]) == indent && lines[i].TrimStart().StartsWith("-"))
            {
                string item = lines[i].Substring(indent + 1);
                string trimmed = item.Trim();
                if (trimmed.Length == 0) { i++; list.Add(i < lines.Count && Indent(lines[i]) > indent ? ParseBlock(lines, ref i, Indent(lines[i])) : ""); continue; }
                int colon = KeyColon(trimmed);
                if (colon > 0 && !trimmed.StartsWith("{"))
                {
                    // "- key: value" starts a mapping item; its further keys sit at indent + 2.
                    int childIndent = indent + 2;
                    lines[i] = new string(' ', childIndent) + trimmed;
                    list.Add(ParseBlock(lines, ref i, childIndent));
                }
                else { list.Add(Scalar(trimmed)); i++; }
            }
            return list;
        }

        private static int KeyColon(string s)
        {
            if (s.StartsWith("'") || s.StartsWith("\"")) return -1;
            int c = s.IndexOf(':');
            if (c < 0) return -1;
            return c + 1 == s.Length || s[c + 1] == ' ' ? c : -1;
        }

        public static object Scalar(string s)
        {
            s = s.Trim();
            if (s.StartsWith("{") && s.EndsWith("}"))
            {
                var map = new Dictionary<string, object>();
                foreach (string part in SplitFlow(s.Substring(1, s.Length - 2)))
                {
                    int c = part.IndexOf(':');
                    if (c < 0) continue;
                    map[part.Substring(0, c).Trim()] = Scalar(part.Substring(c + 1));
                }
                return map;
            }
            if (s.StartsWith("[") && s.EndsWith("]"))
                return SplitFlow(s.Substring(1, s.Length - 2)).Where(p => p.Trim().Length > 0).Select(p => Scalar(p)).ToList();
            if (s.Length >= 2 && s.StartsWith("'") && s.EndsWith("'")) return s.Substring(1, s.Length - 2).Replace("''", "'");
            if (s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\"")) return s.Substring(1, s.Length - 2).Replace("\\\"", "\"");
            return s;
        }

        private static IEnumerable<string> SplitFlow(string s)
        {
            int depth = 0, start = 0;
            for (int k = 0; k < s.Length; k++)
            {
                if (s[k] == '{' || s[k] == '[') depth++;
                else if (s[k] == '}' || s[k] == ']') depth--;
                else if (s[k] == ',' && depth == 0) { yield return s.Substring(start, k - start); start = k + 1; }
            }
            if (start < s.Length) yield return s.Substring(start);
        }
    }

    public static class Assets
    {
        // The project root (the folder holding Assets/): CITY_GAME_ROOT, else the working directory.
        public static readonly string Root = System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("CITY_GAME_ROOT") ?? ".").TrimEnd('/') + "/";
        private static Dictionary<string, string> s_GuidToPath;
        private static readonly Dictionary<string, Object> s_Loaded = new();

        public static Dictionary<string, string> GuidToPath
        {
            get
            {
                if (s_GuidToPath != null) return s_GuidToPath;
                s_GuidToPath = new Dictionary<string, string>();
                foreach (string meta in Directory.EnumerateFiles(Root + "Assets", "*.meta", SearchOption.AllDirectories))
                {
                    foreach (string line in File.ReadLines(meta))
                    {
                        if (!line.StartsWith("guid: ")) continue;
                        s_GuidToPath[line.Substring(6).Trim()] = meta.Substring(Root.Length, meta.Length - Root.Length - 5);
                        break;
                    }
                }
                return s_GuidToPath;
            }
        }

        public static string ScriptClass(Dictionary<string, object> data)
        {
            if (data.TryGetValue("m_Script", out object s) && s is Dictionary<string, object> r
                && r.TryGetValue("guid", out object g) && GuidToPath.TryGetValue((string)g, out string path))
                return Path.GetFileNameWithoutExtension(path);
            return null;
        }

        public static Object Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (s_Loaded.TryGetValue(path, out Object cached)) return cached;
            if (!path.EndsWith(".asset") || !File.Exists(Root + path)) return null;
            Dictionary<string, object> data = Yaml.ParseAsset(Root + path);
            string cls = ScriptClass(data);
            Type type = cls == null ? null : typeof(Assets).Assembly.GetTypes().FirstOrDefault(t => t.Name == cls && typeof(ScriptableObject).IsAssignableFrom(t));
            Object result;
            if (type == null)
            {
                var y = (YamlAsset)Activator.CreateInstance(typeof(YamlAsset), true);
                y.ClassName = cls;
                y.Data = data;
                result = y;
                s_Loaded[path] = result;
            }
            else
            {
                var o = (ScriptableObject)Activator.CreateInstance(type, true);
                s_Loaded[path] = o;
                Fill(o, data);
                Lifecycle.Call(o, "Awake");
                Lifecycle.Call(o, "OnEnable");
                result = o;
            }
            result.name = data.TryGetValue("m_Name", out object n) ? n as string ?? "" : "";
            return result;
        }

        public static Object Reference(object node)
        {
            if (node is Dictionary<string, object> r && r.TryGetValue("guid", out object g)
                && GuidToPath.TryGetValue((string)g, out string path)) return Load(path);
            return null;
        }

        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Fill(object target, Dictionary<string, object> data)
        {
            for (Type t = target.GetType(); t != null && t != typeof(Object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Fields | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                    if (!data.TryGetValue(f.Name, out object node)) continue;
                    f.SetValue(target, Convert(f.FieldType, node));
                }
            }
        }

        public static object Convert(Type t, object node)
        {
            if (typeof(Object).IsAssignableFrom(t)) return Reference(node);
            if (t == typeof(string)) return node as string ?? "";
            if (t == typeof(bool)) return (node as string) == "1" || (node as string) == "true";
            if (t.IsEnum) return Enum.ToObject(t, long.Parse((string)node, CultureInfo.InvariantCulture));
            if (t == typeof(float)) return float.Parse((string)node, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return double.Parse((string)node, CultureInfo.InvariantCulture);
            if (t.IsPrimitive) return System.Convert.ChangeType(long.Parse((string)node, CultureInfo.InvariantCulture), t);
            if (t == typeof(Vector2Int) && node is Dictionary<string, object> v)
                return new Vector2Int(int.Parse((string)v["x"]), int.Parse((string)v["y"]));
            Type element = t.IsArray ? t.GetElementType()
                : t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) ? t.GetGenericArguments()[0] : null;
            if (element != null && element.IsPrimitive && node is string hex)
            {
                // Unity writes primitive arrays as little-endian hex.
                int size = System.Runtime.InteropServices.Marshal.SizeOf(element);
                var bytes = new List<byte>();
                for (int k = 0; k + 1 < hex.Length && Uri.IsHexDigit(hex[k]) && Uri.IsHexDigit(hex[k + 1]); k += 2)
                    bytes.Add(System.Convert.ToByte(hex.Substring(k, 2), 16));
                int count = bytes.Count / size;
                Array a = Array.CreateInstance(element, count);
                Buffer.BlockCopy(bytes.ToArray(), 0, a, 0, count * size);
                if (t.IsArray) return a;
                var l = (IList)Activator.CreateInstance(t);
                foreach (object item in a) l.Add(item);
                return l;
            }
            if (element != null)
            {
                var items = node as List<object> ?? new List<object>();
                if (t.IsArray)
                {
                    Array a = Array.CreateInstance(element, items.Count);
                    for (int k = 0; k < items.Count; k++) a.SetValue(Convert(element, items[k]), k);
                    return a;
                }
                var list = (IList)Activator.CreateInstance(t);
                foreach (object item in items) list.Add(Convert(element, item));
                return list;
            }
            object value = Activator.CreateInstance(t, true);
            if (node is Dictionary<string, object> d) Fill(value, d);
            return value;
        }
    }
}

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : Object => Harness.Assets.Load(path) as T;

        public static string GUIDToAssetPath(string guid) => Harness.Assets.GuidToPath.TryGetValue(guid, out string p) ? p : "";

        public static string[] FindAssets(string filter)
        {
            string type = filter.StartsWith("t:") ? filter.Substring(2) : null;
            var result = new List<string>();
            foreach (KeyValuePair<string, string> pair in Harness.Assets.GuidToPath)
            {
                if (!pair.Value.EndsWith(".asset") || !File.Exists(Harness.Assets.Root + pair.Value)) continue;
                if (type != null)
                {
                    Dictionary<string, object> data;
                    try { data = Harness.Yaml.ParseAsset(Harness.Assets.Root + pair.Value); }
                    catch { continue; }
                    if (Harness.Assets.ScriptClass(data) != type) continue;
                }
                result.Add(pair.Key);
            }
            return result.ToArray();
        }
    }

    public sealed class SerializedObject
    {
        public readonly Object targetObject;
        public SerializedObject(Object target) { targetObject = target; }

        public SerializedProperty FindProperty(string name)
        {
            if (targetObject is Harness.YamlAsset y)
                return SerializedProperty.ForYaml(y.Data, name);    // fields missing from old assets read as defaults, like Unity
            for (Type t = targetObject.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return SerializedProperty.ForField(targetObject, f);
            }
            return null;
        }

        public bool ApplyModifiedPropertiesWithoutUndo() => true;
        public bool ApplyModifiedProperties() => true;
        public void Update() { }
    }

    public sealed class SerializedProperty
    {
        private Func<object> m_Get;
        private Action<object> m_Set;
        private bool m_Yaml;
        private Type m_Type;    // reflection mode only

        internal static SerializedProperty ForYaml(Dictionary<string, object> owner, string key) =>
            new SerializedProperty { m_Yaml = true, m_Get = () => owner.TryGetValue(key, out object v) ? v : null, m_Set = v => owner[key] = v };

        internal static SerializedProperty ForYamlItem(List<object> owner, int index) =>
            new SerializedProperty { m_Yaml = true, m_Get = () => owner[index], m_Set = v => owner[index] = v };

        internal static SerializedProperty ForField(object owner, FieldInfo f) =>
            new SerializedProperty { m_Type = f.FieldType, m_Get = () => f.GetValue(owner), m_Set = v => f.SetValue(owner, v) };

        private string Text => m_Get() as string ?? "";
        private static float Num(string s) => s.Length == 0 ? 0f : float.Parse(s, CultureInfo.InvariantCulture);

        public string stringValue
        {
            get => m_Yaml ? Text : (string)m_Get();
            set => m_Set(value);
        }

        public float floatValue
        {
            get => m_Yaml ? Num(Text) : System.Convert.ToSingle(m_Get());
            set => m_Set(m_Yaml ? (object)value.ToString("R", CultureInfo.InvariantCulture) : value);
        }

        public int intValue
        {
            get => m_Yaml ? (int)Num(Text) : System.Convert.ToInt32(m_Get());
            set => m_Set(m_Yaml ? (object)value.ToString(CultureInfo.InvariantCulture) : System.Convert.ChangeType(value, m_Type.IsEnum ? Enum.GetUnderlyingType(m_Type) : m_Type));
        }

        public bool boolValue
        {
            get => m_Yaml ? Text == "1" : (bool)m_Get();
            set => m_Set(m_Yaml ? (object)(value ? "1" : "0") : value);
        }

        public int enumValueIndex
        {
            get => m_Yaml ? (int)Num(Text) : System.Convert.ToInt32(m_Get());
            set => m_Set(m_Yaml ? (object)value.ToString() : Enum.ToObject(m_Type, value));
        }

        public Color colorValue
        {
            get
            {
                if (!m_Yaml) return (Color)m_Get();
                var d = (Dictionary<string, object>)m_Get();
                float F(string k) => float.Parse((string)d[k], CultureInfo.InvariantCulture);
                return new Color(F("r"), F("g"), F("b"), F("a"));
            }
        }

        public Vector2Int vector2IntValue
        {
            get
            {
                if (!m_Yaml) return (Vector2Int)m_Get();
                var d = (Dictionary<string, object>)m_Get();
                return new Vector2Int(int.Parse((string)d["x"]), int.Parse((string)d["y"]));
            }
            set => m_Set(m_Yaml ? (object)new Dictionary<string, object> { ["x"] = value.x.ToString(), ["y"] = value.y.ToString() } : value);
        }

        public Object objectReferenceValue => m_Yaml ? Harness.Assets.Reference(m_Get()) : m_Get() as Object;

        public int arraySize => m_Yaml ? (m_Get() as List<object>)?.Count ?? 0 : (m_Get() as IList)?.Count ?? 0;

        public SerializedProperty GetArrayElementAtIndex(int index)
        {
            if (m_Yaml) return ForYamlItem((List<object>)m_Get(), index);
            var list = (IList)m_Get();
            Type element = m_Type.IsArray ? m_Type.GetElementType() : m_Type.GetGenericArguments()[0];
            return new SerializedProperty { m_Type = element, m_Get = () => list[index], m_Set = v => list[index] = v };
        }

        public SerializedProperty FindPropertyRelative(string name)
        {
            if (m_Yaml)
            {
                var d = m_Get() as Dictionary<string, object>;
                return d != null && d.ContainsKey(name) ? ForYaml(d, name) : null;
            }
            object owner = m_Get();
            FieldInfo f = owner?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f == null ? null : ForField(owner, f);
        }
    }
}
