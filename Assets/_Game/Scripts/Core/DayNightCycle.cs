using System;
using UnityEngine;
using UnityEngine.Tilemaps;

// Cosmetic day/night cycle (M18d): one cycle per game month, derived from the calendar (so a load shows the
// right time of day and nothing is saved), frozen while paused. It moves the sun, the ambient light, the
// tint of the ground / road / pipe / zone tilemaps (sprite-unlit, so lights don't reach them) and the
// window glow of the shared Kit material. The sim never reads any of it. Info views show in daylight, and
// "Lock to day" (a player setting in PlayerPrefs) turns the cycle off. Created at runtime by GameManager.
public sealed class DayNightCycle : MonoBehaviour
{

    // Phase 0 = dawn; the cycle is shifted so day 1 of a month starts in the morning, and `k_DayBias`
    // gives the day more of the cycle than the night (full daylight from sun height 0 up).
    private const float k_StartOffset = 0.07f;
    private const float k_DayBias = 0.25f;
    private const float k_BlendSpeed = 3f;          // night factor units per second (views / lock fade over ~0.3 s)
    private const float k_MoonElevation = 38f, k_MoonAzimuth = 200f;
    private static readonly Color s_Noon = new Color(1f, 0.97f, 0.90f), s_Twilight = new Color(1f, 0.60f, 0.38f), s_Moon = new Color(0.50f, 0.60f, 0.95f);
    private static readonly Color s_NightTint = new Color(0.42f, 0.50f, 0.74f), s_DuskTint = new Color(1f, 0.86f, 0.76f);

    private TimeManager m_Time;
    private InfoOverlay m_Overlay;
    private Material m_Kit;
    private Light m_Sun;
    private Tilemap[] m_Tilemaps = Array.Empty<Tilemap>();
    private float m_BaseSunIntensity = 1.1f, m_BaseAmbient = 1f;
    private Color m_BaseSunColor = Color.white;
    private float m_Night;                           // the night factor shown now (0 day .. 1 night)
    private float m_AppliedNight = -1f, m_AppliedPhase = -1f, m_AppliedGlow = -1f, m_AppliedYaw;
    private Color m_AppliedTint = Color.magenta;
    private float? m_DebugPhase;

    public float Night => m_Night;
    public float Phase { get; private set; }

    // Player setting: true keeps the city in daylight.
    public static bool LockToDay
    {
        get => GameSettings.LockToDay;
        set => GameSettings.LockToDay = value;
    }

    // Debug: pin the cycle at a phase (0.25 noon, 0.5 dusk, 0.75 midnight), or null to follow the calendar.
    public float? DebugPhase
    {
        get => m_DebugPhase;
        set => m_DebugPhase = value;
    }

    public void Init(TimeManager time, InfoOverlay overlay, Material kit)
    {
        m_Time = time;
        m_Overlay = overlay;
        m_Kit = kit;

        foreach (Light light in FindObjectsByType<Light>())
        {
            if (light.type == LightType.Directional) { m_Sun = light; break; }
        }
        if (m_Sun != null)
        {
            m_BaseSunIntensity = m_Sun.intensity;
            m_BaseSunColor = m_Sun.color;
        }
        m_BaseAmbient = RenderSettings.ambientIntensity;

        var found = new System.Collections.Generic.List<Tilemap>();
        foreach (Tilemap map in FindObjectsByType<Tilemap>(FindObjectsInactive.Include))
        {
            if (map.name == "Ground" || map.name == "Roads" || map.name == "Pipes" || map.name == "Zones") found.Add(map);
        }
        m_Tilemaps = found.ToArray();
        Apply(0f, force: true);
    }

    private void Update()
    {
        if (m_Time == null) return;

        int cycle = Mathf.Max(1, m_Time.DaysPerMonth);
        float phase = m_DebugPhase ?? CalendarMath.DayPhase(m_Time.Day, m_Time.DayFraction, cycle) + k_StartOffset;
        phase -= Mathf.Floor(phase);
        Phase = phase;

        float target = CalendarMath.NightFactor(phase, k_DayBias);
        if (LockToDay || (m_Overlay != null && m_Overlay.Shown != InfoOverlay.View.Off)) target = 0f;
        m_Night = Mathf.MoveTowards(m_Night, target, k_BlendSpeed * Time.unscaledDeltaTime);
        Apply(phase, force: false);
    }

    private void Apply(float phase, bool force)
    {
        // The sun and ambient follow every small step; the tilemap tint and window glow only when they change.
        if (!force && Mathf.Abs(phase - m_AppliedPhase) < 0.0015f && Mathf.Abs(m_Night - m_AppliedNight) < 0.002f && Mathf.Approximately(m_AppliedYaw, IsoCameraController.YawOffset)) return;
        m_AppliedPhase = phase;
        m_AppliedYaw = IsoCameraController.YawOffset;

        float sun = Mathf.Clamp01(CalendarMath.SunHeight(phase));
        float twilight = 1f - Mathf.SmoothStep(0f, 0.35f, Mathf.Abs(CalendarMath.SunHeight(phase)));
        float day = 1f - m_Night;

        if (m_Sun != null)
        {
            float dayElevation = Mathf.Lerp(12f, 52f, sun);
            float dayAzimuth = 240f + Mathf.Lerp(-30f, 30f, Mathf.Clamp01(phase * 2f));
            m_Sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(dayElevation, k_MoonElevation, m_Night), Mathf.Lerp(dayAzimuth, k_MoonAzimuth, m_Night) + m_AppliedYaw, 0f);   // M20d: turns with the camera
            Color sunColor = Color.Lerp(s_Noon, s_Twilight, twilight);
            m_Sun.color = Color.Lerp(sunColor, s_Moon, m_Night) * (m_BaseSunColor.maxColorComponent > 0f ? m_BaseSunColor : Color.white);
            m_Sun.intensity = Mathf.Lerp(m_BaseSunIntensity * (0.6f + 0.4f * sun), 0.30f, m_Night);
        }
        RenderSettings.ambientIntensity = Mathf.Lerp(m_BaseAmbient, m_BaseAmbient * 0.35f, m_Night);

        // Sprite-unlit tilemaps ignore lights: multiply their colour instead (floor keeps the city readable).
        Color tint = Color.Lerp(Color.Lerp(Color.white, s_DuskTint, twilight * day), s_NightTint, m_Night);
        if (force || !Approximately(tint, m_AppliedTint))
        {
            m_AppliedTint = tint;
            foreach (Tilemap map in m_Tilemaps)
            {
                if (map != null) map.color = tint;
            }
        }

        float glow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.9f, m_Night)) * 1.6f;
        if (m_Kit != null && (force || Mathf.Abs(glow - m_AppliedGlow) > 0.004f))
        {
            m_AppliedGlow = glow;
            m_Kit.SetColor("_EmissionColor", new Color(glow, glow, glow, 1f));
        }
        m_AppliedNight = m_Night;
    }

    private static bool Approximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.003f && Mathf.Abs(a.g - b.g) < 0.003f && Mathf.Abs(a.b - b.b) < 0.003f;
    }

    // Leaves the shared asset the way it was authored (runtime edits of a material asset stick in the Editor).
    private void Restore()
    {
        if (m_Kit != null) m_Kit.SetColor("_EmissionColor", Color.black);
        if (m_Sun != null)
        {
            m_Sun.color = m_BaseSunColor;
            m_Sun.intensity = m_BaseSunIntensity;
        }
        RenderSettings.ambientIntensity = m_BaseAmbient;
        foreach (Tilemap map in m_Tilemaps)
        {
            if (map != null) map.color = Color.white;
        }
    }

    private void OnDestroy() => Restore();

    private void OnApplicationQuit() => Restore();
}
