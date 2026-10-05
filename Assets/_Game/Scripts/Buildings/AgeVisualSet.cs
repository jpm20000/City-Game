using System;
using UnityEngine;

// How grown blocks built in one age look (M11). Per zone and level: optional prefab variants (one is
// picked per cell by hash) and a fallback style for the placeholder blocks. GrowthVisuals matches the
// set to an age by AgeId. Prefab contract (GamePlan §12, M18): pivot at the ground centre of a 1x1
// cell, +Y up, 1 unit = 1 cell, fits inside the cell, layer 9, a collider on the root, shared
// materials only (no per-instance materials or MaterialPropertyBlocks), one or two materials.
[CreateAssetMenu(fileName = "AgeVisualSet", menuName = "CityBuilder/Age Visual Set")]
public sealed class AgeVisualSet : ScriptableObject
{
    public enum RoofStyle
    {
        Default,    // today's look: a cap / rooftop box on homes and shops, a chimney on industry
        Pitched,    // a gable roof (ridge along X or Z, picked per cell)
        None,
    }

    [Serializable]
    public struct Style
    {
        [Tooltip("Variants for this zone and level; empty = the placeholder block below.")]
        public GameObject[] Prefabs;
        [Tooltip("Body colour = zone colour blended toward this colour by its alpha (alpha 0 = plain zone colour).")]
        public Color Tint;
        public RoofStyle Roof;
        [Tooltip("Roof colour; alpha 0 = the body colour, shaded.")]
        public Color RoofColor;
        [Tooltip("Body height multiplier (1 = today's profile).")]
        public float HeightScale;

        public static Style Plain => new Style { Prefabs = Array.Empty<GameObject>(), Tint = Color.clear, RoofColor = Color.clear, HeightScale = 1f };
    }

    [Tooltip("AgeDefinition.Id this set dresses.")]
    [SerializeField] private string m_AgeId = "";
    [Tooltip("Levels 1..3.")]
    [SerializeField] private Style[] m_Residential = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_Commercial = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_Industrial = { Style.Plain, Style.Plain, Style.Plain };

    [Header("Low / High density (M23)")]
    [Tooltip("Levels 1..3 for Low and High blocks. A slot with no prefab falls back to the Medium style, drawn lower (Low) or taller (High) by GrowthVisuals.")]
    [SerializeField] private Style[] m_ResidentialLow = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_CommercialLow = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_IndustrialLow = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_ResidentialHigh = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_CommercialHigh = { Style.Plain, Style.Plain, Style.Plain };
    [SerializeField] private Style[] m_IndustrialHigh = { Style.Plain, Style.Plain, Style.Plain };

    public string AgeId => m_AgeId;

    // The style for a density (M23). Own = the set has prefabs for this density; otherwise it is the Medium style and
    // the caller stretches it (GrowthVisuals.HeightFor).
    public Style Get(ZoneType zone, int level, Density density, out bool own)
    {
        own = false;
        if (density != Density.Medium)
        {
            Style[] table = zone switch
            {
                ZoneType.Commercial => density == Density.Low ? m_CommercialLow : m_CommercialHigh,
                ZoneType.Industrial => density == Density.Low ? m_IndustrialLow : m_IndustrialHigh,
                _ => density == Density.Low ? m_ResidentialLow : m_ResidentialHigh,
            };
            int i = level - 1;
            if (table != null && i >= 0 && i < table.Length && table[i].Prefabs != null && table[i].Prefabs.Length > 0)
            {
                own = true;
                Style style = table[i];
                if (style.HeightScale <= 0f) style.HeightScale = 1f;
                return style;
            }
        }
        return Get(zone, level);
    }

    // Missing entries fall back to the plain style (today's look).
    public Style Get(ZoneType zone, int level)
    {
        Style[] table = zone switch
        {
            ZoneType.Commercial => m_Commercial,
            ZoneType.Industrial => m_Industrial,
            _ => m_Residential,
        };
        int i = level - 1;
        if (table == null || i < 0 || i >= table.Length) return Style.Plain;
        Style style = table[i];
        if (style.HeightScale <= 0f) style.HeightScale = 1f;
        style.Prefabs ??= Array.Empty<GameObject>();
        return style;
    }
}
