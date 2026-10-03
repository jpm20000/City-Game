// Minimal stand-ins for the UnityEngine / UnityEditor APIs the pure Grid + Simulation code and its
// EditMode tests use, so they compile and run under Mono without the Unity Editor.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = "";
        public static void DestroyImmediate(Object o) { if (o != null) o.m_Destroyed = true; }
        public static void Destroy(Object o) { DestroyImmediate(o); }
        internal bool m_Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.m_Destroyed;
            bool bn = ReferenceEquals(b, null) || b.m_Destroyed;
            if (an || bn) return an && bn;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public static implicit operator bool(Object o) => !(o == null);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => (T)CreateInstance(typeof(T));
        public static ScriptableObject CreateInstance(Type t)
        {
            var o = (ScriptableObject)Activator.CreateInstance(t, true);
            Harness.Lifecycle.Call(o, "Awake");
            Harness.Lifecycle.Call(o, "OnEnable");
            return o;
        }
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new Vector2Int(0, 0);
        public static Vector2Int one => new Vector2Int(1, 1);
        public static Vector2Int up => new Vector2Int(0, 1);
        public static Vector2Int down => new Vector2Int(0, -1);
        public static Vector2Int left => new Vector2Int(-1, 0);
        public static Vector2Int right => new Vector2Int(1, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new Vector2Int(a.x - b.x, a.y - b.y);
        public static Vector2Int operator *(Vector2Int a, int k) => new Vector2Int(a.x * k, a.y * k);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
        public bool Equals(Vector2Int o) => this == o;
        public override bool Equals(object o) => o is Vector2Int v && this == v;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => $"({x}, {y})";
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static implicit operator Vector2(Vector2Int v) => new Vector2(v.x, v.y);
        public static float Distance(Vector2 a, Vector2 b) { float dx = a.x - b.x, dy = a.y - b.y; return (float)Math.Sqrt(dx * dx + dy * dy); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 zero => new Vector3(0, 0, 0);
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Max(params int[] v) { int m = v[0]; foreach (int x in v) if (x > m) m = x; return m; }
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Max(params float[] v) { float m = v[0]; foreach (float x in v) if (x > m) m = x; return m; }
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Min(params int[] v) { int m = v[0]; foreach (int x in v) if (x < m) m = x; return m; }
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Min(params float[] v) { float m = v[0]; foreach (float x in v) if (x < m) m = x; return m; }
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static float Round(float v) => (float)Math.Round(v, MidpointRounding.ToEven);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);
        public const float Epsilon = float.Epsilon;
    }

    public static class Debug
    {
        public static void Log(object m) => Console.WriteLine(m);
        public static void LogWarning(object m) => Console.WriteLine("WARN " + m);
        public static void LogError(object m) => Console.WriteLine("ERROR " + m);
    }

    public static class Application { public static string dataPath => Harness.Assets.Root + "Assets"; }

    public static class Time
    {
        public static float realtimeSinceStartup => (float)Harness.Clock.Elapsed.TotalSeconds;
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) => Harness.Json.ToJson(o);
        public static T FromJson<T>(string s) => Harness.Json.FromJson<T>(s);
    }

    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.Field)] public class MinAttribute : Attribute { public MinAttribute(float v) { } }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public class TextAreaAttribute : Attribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.Class)] public class CreateAssetMenuAttribute : Attribute { public string fileName; public string menuName; public int order; }
}

namespace Harness
{
    public static class Clock { public static readonly System.Diagnostics.Stopwatch Elapsed0 = System.Diagnostics.Stopwatch.StartNew(); public static TimeSpan Elapsed => Elapsed0.Elapsed; }

    public static class Lifecycle
    {
        public static void Call(object o, string method)
        {
            var m = o.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            m?.Invoke(o, null);
        }
    }
}
namespace UnityEngine
{
    public partial struct Vector3Ops { }
}
