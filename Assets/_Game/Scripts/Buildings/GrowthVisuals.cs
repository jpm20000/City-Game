using System;
using System.Collections.Generic;
using UnityEngine;

// Mirrors grown zone cells (GridData building levels) as placeholder blocks. Each zone has its own
// silhouette per level (houses -> apartments -> towers, shops -> offices -> high-rises, low wide
// factories with chimneys), with a per-cell size jitter so rows don't look stamped. New and upgraded
// cells "pop" (scale overshoot) so growth is noticeable.
public sealed class GrowthVisuals : MonoBehaviour
{
    private const int k_BuildingsLayer = 9;
    private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] private Material m_Material;
    [SerializeField] private Color m_ChimneyColor = new Color(0.36f, 0.36f, 0.40f);
    [SerializeField, Range(0f, 1f)] private float m_RoofShade = 0.62f;
    [SerializeField, Range(0f, 0.5f)] private float m_HeightJitter = 0.15f;

    [Header("Level-up pop")]
    [SerializeField] private float m_PopDuration = 0.35f;
    [SerializeField, Range(0f, 1f)] private float m_PopStartScale = 0.6f;

    // Footprint (fraction of a cell) and body height per level 1..3.
    private struct Profile
    {
        public float Footprint;
        public float Height;
        public Profile(float footprint, float height) { Footprint = footprint; Height = height; }
    }

    private static readonly Profile[] s_Residential = { new(0.70f, 0.40f), new(0.64f, 0.95f), new(0.52f, 1.80f) };
    private static readonly Profile[] s_Commercial = { new(0.80f, 0.50f), new(0.72f, 1.15f), new(0.60f, 2.20f) };
    private static readonly Profile[] s_Industrial = { new(0.82f, 0.42f), new(0.86f, 0.62f), new(0.90f, 0.85f) };

    private sealed class Grown
    {
        public Transform Body;
        public Transform Roof;      // R/C: darker cap; I: chimney
        public ZoneType Zone;
        public int Level;
        public float Jitter;        // per-cell height multiplier
        public float PopTime = -1f; // seconds into the pop; < 0 = idle
    }

    private readonly Dictionary<Vector2Int, Grown> m_Cells = new();
    private readonly List<Vector2Int> m_Popping = new();
    private GridData m_Grid;
    private MaterialPropertyBlock m_Block;
    private Func<Vector2Int, Color?> m_ColorOverride;

    public void Init(GridData grid)
    {
        m_Grid = grid;
        m_Block = new MaterialPropertyBlock();
        m_Grid.OnCellChanged += SyncCell;
    }

    private void OnDestroy()
    {
        if (m_Grid != null) m_Grid.OnCellChanged -= SyncCell;
    }

    // Info views recolour grown buildings (the ground overlay is mostly hidden under them). Return
    // null from the function to keep a cell's zone colour; pass null to restore all zone colours.
    public void SetColorOverride(Func<Vector2Int, Color?> colorOverride)
    {
        m_ColorOverride = colorOverride;
        RefreshColors();
    }

    public void RefreshColors()
    {
        foreach (KeyValuePair<Vector2Int, Grown> pair in m_Cells)
        {
            ApplyColors(pair.Key, pair.Value);
        }
    }

    private void Update()
    {
        for (int i = m_Popping.Count - 1; i >= 0; i--)
        {
            Vector2Int cell = m_Popping[i];
            if (!m_Cells.TryGetValue(cell, out Grown grown))
            {
                m_Popping.RemoveAt(i);
                continue;
            }

            grown.PopTime += Time.deltaTime;
            float t = Mathf.Clamp01(grown.PopTime / m_PopDuration);
            ApplyTransform(cell, grown, Mathf.LerpUnclamped(m_PopStartScale, 1f, EaseOutBack(t)));
            if (t >= 1f)
            {
                grown.PopTime = -1f;
                m_Popping.RemoveAt(i);
            }
        }
    }

    private void SyncCell(Vector2Int cell)
    {
        int level = m_Grid.GetBuildingLevel(cell);
        m_Cells.TryGetValue(cell, out Grown grown);

        if (level == 0)
        {
            if (grown != null) DestroyGrown(grown);
            m_Cells.Remove(cell);
            return;
        }

        ZoneType zone = m_Grid.GetZone(cell);
        if (grown == null)
        {
            grown = new Grown { Body = CreateBlock($"Grown_{cell.x}_{cell.y}", true), Jitter = CellJitter(cell) };
            m_Cells[cell] = grown;
        }

        // Zone changes swap the roof piece (cap vs chimney).
        if (grown.Roof == null || grown.Zone != zone)
        {
            if (grown.Roof != null) Destroy(grown.Roof.gameObject);
            grown.Roof = CreateBlock(zone == ZoneType.Industrial ? "Chimney" : "Roof", false);
            grown.Roof.SetParent(grown.Body.parent, false);
            grown.Zone = zone;
        }

        // Only growth pops; a rezone recolour or a level drop updates instantly.
        bool grew = level > grown.Level;
        grown.Level = level;
        if (grew)
        {
            if (grown.PopTime < 0f) m_Popping.Add(cell);
            grown.PopTime = 0f;
            ApplyTransform(cell, grown, m_PopStartScale);
        }
        else if (grown.PopTime < 0f)
        {
            ApplyTransform(cell, grown, 1f);
        }

        ApplyColors(cell, grown);
    }

    private void ApplyColors(Vector2Int cell, Grown grown)
    {
        Color color = m_ColorOverride?.Invoke(cell) ?? ZonePalette.Get(grown.Zone);
        SetColor(grown.Body, color);
        SetColor(grown.Roof, grown.Zone == ZoneType.Industrial ? m_ChimneyColor : color * m_RoofShade);
    }

    // Everything scales around the cell's ground point so buildings grow up out of the ground.
    private void ApplyTransform(Vector2Int cell, Grown grown, float scale)
    {
        Profile profile = ProfileFor(grown.Zone, grown.Level);
        float footprint = profile.Footprint * scale;
        float height = profile.Height * grown.Jitter * scale;
        Vector3 ground = new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);

        grown.Body.localScale = new Vector3(footprint, height, footprint);
        grown.Body.position = ground + Vector3.up * (height * 0.5f);

        if (grown.Zone == ZoneType.Industrial)
        {
            // Chimney at the back corner, taller than the shed.
            float width = 0.14f * scale;
            float chimneyHeight = height + 0.45f * scale;
            float offset = footprint * 0.5f - width * 0.5f;
            grown.Roof.localScale = new Vector3(width, chimneyHeight, width);
            grown.Roof.position = ground + new Vector3(-offset, chimneyHeight * 0.5f, -offset);
        }
        else
        {
            // Overhanging cap on houses; an inset rooftop box on taller buildings.
            float capFootprint = grown.Level == 1 ? footprint * 1.08f : footprint * 0.55f;
            float capHeight = (grown.Level == 1 ? 0.10f : 0.14f) * scale;
            grown.Roof.localScale = new Vector3(capFootprint, capHeight, capFootprint);
            grown.Roof.position = ground + Vector3.up * (height + capHeight * 0.5f);
        }
    }

    private static Profile ProfileFor(ZoneType zone, int level)
    {
        Profile[] table = zone switch
        {
            ZoneType.Commercial => s_Commercial,
            ZoneType.Industrial => s_Industrial,
            _ => s_Residential,
        };
        return table[Mathf.Clamp(level, 1, table.Length) - 1];
    }

    // Deterministic per-cell variation (same city -> same skyline after a reload).
    private float CellJitter(Vector2Int cell)
    {
        uint h = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        float unit = (h & 0xFFFF) / 65535f;
        return 1f + (unit * 2f - 1f) * m_HeightJitter;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // Only the body keeps its collider: selection raycasts should land on the main block.
    private Transform CreateBlock(string name, bool withCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = k_BuildingsLayer;
        go.transform.SetParent(transform, false);
        if (!withCollider) Destroy(go.GetComponent<Collider>());
        if (m_Material != null) go.GetComponent<MeshRenderer>().sharedMaterial = m_Material;
        return go.transform;
    }

    private void SetColor(Transform block, Color color)
    {
        color.a = 1f;
        m_Block.SetColor(s_BaseColorId, color);
        block.GetComponent<MeshRenderer>().SetPropertyBlock(m_Block);
    }

    private static void DestroyGrown(Grown grown)
    {
        if (grown.Roof != null) Destroy(grown.Roof.gameObject);
        Destroy(grown.Body.gameObject);
    }
}
