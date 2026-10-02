using System;
using System.Collections.Generic;
using UnityEngine;

// Mirrors grown zone cells (GridData building levels) as placeholder blocks. Each zone has its own
// silhouette per level (houses -> apartments -> towers, shops -> offices -> high-rises, low wide
// factories with chimneys), with a per-cell size jitter so rows don't look stamped. New and upgraded
// cells "pop" (scale overshoot) so growth is noticeable. Blocks are pooled (hidden, not destroyed):
// creating or destroying GameObjects costs milliseconds per frame once a big map holds thousands.
// Colours are shared materials (one per colour), not MaterialPropertyBlocks: a property block makes a
// renderer incompatible with the SRP Batcher and the GPU Resident Drawer, which on a full 96x96 map
// cost ~20 ms of render thread per frame.
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

    // One pooled cube: the body keeps its collider (selection raycasts); roofs have none.
    private sealed class Block
    {
        public Transform Transform;
        public MeshRenderer Renderer;
        public Collider Collider;
    }

    private sealed class Grown
    {
        public Block Body;
        public Block Roof;          // R/C: darker cap; I: chimney
        public ZoneType Zone;
        public int Level;
        public float Jitter;        // per-cell height multiplier
        public float PopTime = -1f; // seconds into the pop; < 0 = idle
    }

    private readonly Dictionary<Vector2Int, Grown> m_Cells = new();
    private readonly List<Vector2Int> m_Popping = new();
    private readonly Stack<Block> m_FreeBodies = new();
    private readonly Stack<Block> m_FreeRoofs = new();
    private GridData m_Grid;
    private readonly Dictionary<Color, Material> m_Materials = new();
    private Func<Vector2Int, Color?> m_ColorOverride;

    public void Init(GridData grid)
    {
        m_Grid = grid;
        m_Grid.OnCellChanged += SyncCell;
        m_Grid.OnResized += ClearAll;
    }

    private void OnDestroy()
    {
        foreach (Material material in m_Materials.Values) Destroy(material);
        m_Materials.Clear();
        if (m_Grid == null) return;
        m_Grid.OnCellChanged -= SyncCell;
        m_Grid.OnResized -= ClearAll;
    }

    // The map was replaced by an empty one; the new city's cells arrive as OnCellChanged. The pool is
    // dropped too, so shrinking from a big map doesn't keep thousands of hidden blocks around.
    private void ClearAll()
    {
        foreach (Grown grown in m_Cells.Values)
        {
            Destroy(grown.Body.Transform.gameObject);
            Destroy(grown.Roof.Transform.gameObject);
        }
        foreach (Block block in m_FreeBodies) Destroy(block.Transform.gameObject);
        foreach (Block block in m_FreeRoofs) Destroy(block.Transform.gameObject);
        m_Cells.Clear();
        m_Popping.Clear();
        m_FreeBodies.Clear();
        m_FreeRoofs.Clear();
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
            if (grown != null)
            {
                Release(grown.Body, m_FreeBodies);
                Release(grown.Roof, m_FreeRoofs);
                m_Cells.Remove(cell);
            }
            return;
        }

        if (grown == null)
        {
            grown = new Grown { Body = Acquire(m_FreeBodies, true), Roof = Acquire(m_FreeRoofs, false), Jitter = CellJitter(cell) };
            m_Cells[cell] = grown;
        }

        // A rezone just reshapes the roof block (cap vs chimney) in ApplyTransform.
        grown.Zone = m_Grid.GetZone(cell);

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

        Transform body = grown.Body.Transform;
        Transform roof = grown.Roof.Transform;
        body.localScale = new Vector3(footprint, height, footprint);
        body.position = ground + Vector3.up * (height * 0.5f);

        if (grown.Zone == ZoneType.Industrial)
        {
            // Chimney at the back corner, taller than the shed.
            float width = 0.14f * scale;
            float chimneyHeight = height + 0.45f * scale;
            float offset = footprint * 0.5f - width * 0.5f;
            roof.localScale = new Vector3(width, chimneyHeight, width);
            roof.position = ground + new Vector3(-offset, chimneyHeight * 0.5f, -offset);
        }
        else
        {
            // Overhanging cap on houses; an inset rooftop box on taller buildings.
            float capFootprint = grown.Level == 1 ? footprint * 1.08f : footprint * 0.55f;
            float capHeight = (grown.Level == 1 ? 0.10f : 0.14f) * scale;
            roof.localScale = new Vector3(capFootprint, capHeight, capFootprint);
            roof.position = ground + Vector3.up * (height + capHeight * 0.5f);
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

    private Block Acquire(Stack<Block> pool, bool isBody)
    {
        if (pool.Count > 0)
        {
            Block block = pool.Pop();
            block.Renderer.enabled = true;
            if (block.Collider != null) block.Collider.enabled = true;
            return block;
        }
        return CreateBlock(isBody);
    }

    // Hiding the renderer (not SetActive) avoids a hierarchy change; the collider goes too so hidden
    // bodies can't be selected.
    private static void Release(Block block, Stack<Block> pool)
    {
        block.Renderer.enabled = false;
        if (block.Collider != null) block.Collider.enabled = false;
        pool.Push(block);
    }

    // Only the body keeps its collider: selection raycasts should land on the main block.
    private Block CreateBlock(bool isBody)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = isBody ? "GrownBody" : "GrownRoof";
        go.layer = k_BuildingsLayer;
        go.transform.SetParent(transform, false);
        Collider collider = go.GetComponent<Collider>();
        if (!isBody)
        {
            Destroy(collider);
            collider = null;
        }
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        return new Block { Transform = go.transform, Renderer = renderer, Collider = collider };
    }

    private void SetColor(Block block, Color color)
    {
        color.a = 1f;
        Material material = MaterialFor(color);
        if (block.Renderer.sharedMaterial != material) block.Renderer.sharedMaterial = material;
    }

    // Zone colours, roof shades and the info views' tints are a small fixed set, so this stays tiny.
    private Material MaterialFor(Color color)
    {
        if (m_Materials.TryGetValue(color, out Material material)) return material;

        material = m_Material != null ? new Material(m_Material) : new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
        material.name = $"Grown {ColorUtility.ToHtmlStringRGB(color)}";
        material.SetColor(s_BaseColorId, color);
        m_Materials.Add(color, material);
        return material;
    }
}
