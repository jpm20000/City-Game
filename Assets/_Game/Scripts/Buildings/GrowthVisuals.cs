using System;
using System.Collections.Generic;
using UnityEngine;

// Mirrors grown zone cells (GridData building levels) as placeholder blocks. Each zone has its own
// silhouette per level (houses -> apartments -> towers, shops -> offices -> high-rises, low wide
// factories with chimneys), with a per-cell size jitter so rows don't look stamped. A cell's look
// also follows the age it was built in (AgeVisualSet, M11): tint, roof style and height, or prefab
// variants that replace the blocks. New, upgraded and redeveloped cells "pop" (scale overshoot) so
// change is noticeable. Blocks and prefab instances are pooled (hidden, not destroyed): creating or
// destroying GameObjects costs milliseconds per frame once a big map holds thousands. Colours are
// shared materials (one per colour), not MaterialPropertyBlocks: a property block makes a renderer
// incompatible with the SRP Batcher and the GPU Resident Drawer, which on a full 96x96 map cost
// ~20 ms of render thread per frame.
public sealed class GrowthVisuals : MonoBehaviour
{
    private const int k_BuildingsLayer = 9;
    private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly AgeVisualSet.Style s_Plain = AgeVisualSet.Style.Plain;

    [SerializeField] private Material m_Material;
    [SerializeField] private Color m_ChimneyColor = new Color(0.36f, 0.36f, 0.40f);
    [SerializeField, Range(0f, 1f)] private float m_RoofShade = 0.62f;
    [SerializeField, Range(0f, 0.5f)] private float m_HeightJitter = 0.15f;

    [Header("Level-up pop")]
    [SerializeField] private float m_PopDuration = 0.35f;
    [SerializeField, Range(0f, 1f)] private float m_PopStartScale = 0.6f;

    [Header("Ages (M11)")]
    [Tooltip("One set per age, matched by AgeId. Cells built in an age without a set use the plain look.")]
    [SerializeField] private AgeVisualSet[] m_AgeVisuals = Array.Empty<AgeVisualSet>();

    // Footprint (fraction of a cell) and body height per level 1..3.
    private struct Profile
    {
        public float Footprint;
        public float Height;
        public Profile(float footprint, float height) { Footprint = footprint; Height = height; }
    }

    private static readonly Profile[] s_Residential = { new(0.70f, 0.40f), new(0.64f, 0.95f), new(0.52f, 1.30f) };
    private static readonly Profile[] s_Commercial = { new(0.80f, 0.50f), new(0.72f, 1.15f), new(0.60f, 1.40f) };
    // M23: Low and High blocks without art of their own are the Medium look drawn lower / taller.
    private const float k_LowHeight = 0.6f;
    private const float k_HighHeight = 1.5f;

    private static readonly Profile[] s_Industrial = { new(0.82f, 0.42f), new(0.86f, 0.62f), new(0.90f, 0.85f) };

    // One pooled cube: the body keeps its collider (selection raycasts); roofs have none.
    private sealed class Block
    {
        public Transform Transform;
        public MeshRenderer Renderer;
        public Collider Collider;
    }

    // One pooled prefab instance; Originals restore its materials after an info view tinted it.
    private sealed class PrefabVisual
    {
        public GameObject Source;
        public Transform Transform;
        public Renderer[] Renderers;
        public Material[][] Originals;
        public Collider[] Colliders;
    }

    private sealed class Grown
    {
        public Block Body;          // placeholder blocks, or null while a prefab is shown
        public Block Roof;          // R/C: darker cap or gable; I: chimney or gable
        public PrefabVisual Prefab;
        public ZoneType Zone;
        public int Level;
        public int Age = -1;        // built age the visual was made for
        public AgeVisualSet.Style Style;
        public float Jitter;        // per-cell height multiplier
        public Density Density;     // the density the visual was made for (M23)
        public float DensityHeight = 1f;    // stretch of the Medium look when the age has no own Low / High art
        public uint Hash;           // per-cell variant / orientation picks
        public float PopTime = -1f; // seconds into the pop; < 0 = idle
    }

    private readonly Dictionary<Vector2Int, Grown> m_Cells = new();
    private readonly List<Vector2Int> m_Popping = new();
    private readonly Stack<Block> m_FreeBodies = new();
    private readonly Stack<Block> m_FreeRoofs = new();
    private readonly Dictionary<GameObject, Stack<PrefabVisual>> m_FreePrefabs = new();
    private readonly Dictionary<Color, Material> m_Materials = new();
    private AgeVisualSet[] m_SetsByAge = Array.Empty<AgeVisualSet>();
    private GridData m_Grid;
    private Func<Vector2Int, Color?> m_ColorOverride;

    // ages null = no age data: every cell uses the plain look.
    public void Init(GridData grid, AgeDatabase ages = null)
    {
        m_Grid = grid;
        m_Grid.OnCellChanged += SyncCell;
        m_Grid.OnResized += ClearAll;

        if (ages == null) return;
        m_SetsByAge = new AgeVisualSet[ages.Count];
        foreach (AgeVisualSet set in m_AgeVisuals)
        {
            if (set == null) continue;
            int index = ages.IndexOf(set.AgeId);
            if (index >= 0) m_SetsByAge[index] = set;
            else Debug.LogWarning($"GrowthVisuals: AgeVisualSet '{set.name}' names unknown age '{set.AgeId}'.", this);
        }
    }

    private void OnDestroy()
    {
        foreach (Material material in m_Materials.Values) Destroy(material);
        m_Materials.Clear();
        if (m_Grid == null) return;
        m_Grid.OnCellChanged -= SyncCell;
        m_Grid.OnResized -= ClearAll;
    }

    // The map was replaced by an empty one; the new city's cells arrive as OnCellChanged. The pools are
    // dropped too, so shrinking from a big map doesn't keep thousands of hidden objects around.
    private void ClearAll()
    {
        foreach (Grown grown in m_Cells.Values)
        {
            if (grown.Body != null) Destroy(grown.Body.Transform.gameObject);
            if (grown.Roof != null) Destroy(grown.Roof.Transform.gameObject);
            if (grown.Prefab != null) Destroy(grown.Prefab.Transform.gameObject);
        }
        foreach (Block block in m_FreeBodies) Destroy(block.Transform.gameObject);
        foreach (Block block in m_FreeRoofs) Destroy(block.Transform.gameObject);
        foreach (Stack<PrefabVisual> pool in m_FreePrefabs.Values)
        {
            foreach (PrefabVisual visual in pool) Destroy(visual.Transform.gameObject);
        }
        m_Cells.Clear();
        m_Popping.Clear();
        m_FreeBodies.Clear();
        m_FreeRoofs.Clear();
        m_FreePrefabs.Clear();
    }

    // Info views recolour grown buildings (the ground overlay is mostly hidden under them). Return
    // null from the function to keep a cell's own colours; pass null to restore all of them.
    // The top of a grown cell's visual in world Y (0 when there is none): the kit's towers reach about 3, the
    // placeholder blocks 0.5-2.5, so markers that sit on a building (fire, plague) read it instead of a fixed height.
    public float VisualTop(Vector2Int cell)
    {
        if (!m_Cells.TryGetValue(cell, out Grown grown)) return 0f;
        if (grown.Prefab != null && grown.Prefab.Renderers.Length > 0) return grown.Prefab.Renderers[0].bounds.max.y;
        if (grown.Body != null) return grown.Body.Renderer.bounds.max.y;
        return 0f;
    }

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
                ReleaseVisual(grown);
                m_Cells.Remove(cell);
            }
            RefaceNeighbours(cell);
            return;
        }

        if (grown == null)
        {
            uint hash = CellHash(cell);
            grown = new Grown { Hash = hash, Jitter = 1f + (((hash & 0xFFFF) / 65535f) * 2f - 1f) * m_HeightJitter };
            m_Cells[cell] = grown;
        }

        // A rezone just reshapes the roof block (cap vs chimney) in ApplyTransform.
        grown.Zone = m_Grid.GetZone(cell);

        // Growth and redevelopment (a new built age) pop; a rezone recolour or a level drop is instant.
        int age = m_Grid.GetBuiltAge(cell);
        Density density = m_Grid.GetDensity(cell);
        bool grew = level > grown.Level || (grown.Level > 0 && (age != grown.Age || density != grown.Density));
        if (level > grown.Level && grown.Level > 0 && AudioController.AllowGrowthSounds)
        {
            AudioController.Play(SfxId.LevelUp, new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f));
        }
        grown.Level = level;
        grown.Age = age;
        grown.Density = density;
        grown.Style = StyleFor(grown.Zone, level, age, density, out bool own);
        grown.DensityHeight = own ? 1f : density == Density.Low ? k_LowHeight : density == Density.High ? k_HighHeight : 1f;
        EnsureVisual(grown);

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

    // A road laid or removed beside a grown cell turns its prefab to face the street (placeholder
    // blocks are symmetric). Cells mid-pop pick the new rotation up on their next frame.
    private void RefaceNeighbours(Vector2Int cell)
    {
        foreach (Vector2Int step in CellUtils.Neighbors4)
        {
            Vector2Int other = cell + step;
            if (m_Cells.TryGetValue(other, out Grown neighbour) && neighbour.Prefab != null && neighbour.PopTime < 0f)
            {
                ApplyTransform(other, neighbour, 1f);
            }
        }
    }

    private AgeVisualSet.Style StyleFor(ZoneType zone, int level, int age, Density density, out bool own)
    {
        own = false;
        AgeVisualSet set = age >= 0 && age < m_SetsByAge.Length ? m_SetsByAge[age] : null;
        return set != null ? set.Get(zone, level, density, out own) : s_Plain;
    }

    // Swaps between placeholder blocks and a prefab variant when the style asks for the other.
    private void EnsureVisual(Grown grown)
    {
        GameObject[] prefabs = grown.Style.Prefabs;
        GameObject prefab = prefabs != null && prefabs.Length > 0 ? prefabs[grown.Hash % (uint)prefabs.Length] : null;

        if (prefab != null)
        {
            if (grown.Body != null)
            {
                Release(grown.Body, m_FreeBodies);
                Release(grown.Roof, m_FreeRoofs);
                grown.Body = null;
                grown.Roof = null;
            }
            if (grown.Prefab != null && grown.Prefab.Source != prefab) ReleasePrefab(grown);
            if (grown.Prefab == null) grown.Prefab = AcquirePrefab(prefab);
        }
        else
        {
            if (grown.Prefab != null) ReleasePrefab(grown);
            if (grown.Body == null)
            {
                grown.Body = Acquire(m_FreeBodies, true);
                grown.Roof = Acquire(m_FreeRoofs, false);
            }
        }
    }

    private void ReleaseVisual(Grown grown)
    {
        if (grown.Body != null) Release(grown.Body, m_FreeBodies);
        if (grown.Roof != null) Release(grown.Roof, m_FreeRoofs);
        if (grown.Prefab != null) ReleasePrefab(grown);
        grown.Body = null;
        grown.Roof = null;
    }

    private void ApplyColors(Vector2Int cell, Grown grown)
    {
        Color? colorOverride = m_ColorOverride?.Invoke(cell);

        if (grown.Prefab != null)
        {
            ApplyPrefabColors(grown.Prefab, colorOverride);
            return;
        }

        AgeVisualSet.Style style = grown.Style;
        Color zoneColor = ZonePalette.Get(grown.Zone);
        Color body = colorOverride ?? Color.Lerp(zoneColor, style.Tint, style.Tint.a);
        SetColor(grown.Body, body);

        Color roof;
        if (grown.Zone == ZoneType.Industrial && style.Roof == AgeVisualSet.RoofStyle.Default) roof = m_ChimneyColor;
        else if (colorOverride == null && style.RoofColor.a > 0f) roof = style.RoofColor;
        else roof = body * m_RoofShade;
        SetColor(grown.Roof, roof);
    }

    // Info views swap every renderer to one shared tinted material; clearing restores the prefab's own.
    private void ApplyPrefabColors(PrefabVisual visual, Color? colorOverride)
    {
        for (int i = 0; i < visual.Renderers.Length; i++)
        {
            Renderer renderer = visual.Renderers[i];
            if (colorOverride == null)
            {
                renderer.sharedMaterials = visual.Originals[i];
                continue;
            }
            Material tinted = MaterialFor(colorOverride.Value);
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++) materials[m] = tinted;
            renderer.sharedMaterials = materials;
        }
    }

    // Everything scales around the cell's ground point so buildings grow up out of the ground.
    private void ApplyTransform(Vector2Int cell, Grown grown, float scale)
    {
        Vector3 ground = new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);

        if (grown.Prefab != null)
        {
            Transform t = grown.Prefab.Transform;
            t.SetPositionAndRotation(ground, Quaternion.Euler(0f, CellUtils.FacingRoad(m_Grid, cell, grown.Hash) * 90f, 0f));
            t.localScale = new Vector3(scale, scale * grown.DensityHeight, scale);
            return;
        }

        Profile profile = ProfileFor(grown.Zone, grown.Level);
        AgeVisualSet.Style style = grown.Style;
        float footprint = profile.Footprint * scale;
        float height = profile.Height * style.HeightScale * grown.Jitter * grown.DensityHeight * scale;

        Transform body = grown.Body.Transform;
        Transform roof = grown.Roof.Transform;
        body.localScale = new Vector3(footprint, height, footprint);
        body.position = ground + Vector3.up * (height * 0.5f);

        grown.Roof.Renderer.enabled = style.Roof != AgeVisualSet.RoofStyle.None;
        switch (style.Roof)
        {
            case AgeVisualSet.RoofStyle.Pitched:
            {
                // A cube turned 45 degrees about the ridge: its upper half is the gable, the lower
                // half hides inside the body. Ridge direction alternates per cell.
                float side = footprint * 0.7071f * 1.02f;
                float length = footprint * 1.04f;
                bool alongX = (grown.Hash & 0x10000) != 0;
                roof.localScale = alongX ? new Vector3(length, side, side) : new Vector3(side, side, length);
                roof.rotation = alongX ? Quaternion.Euler(45f, 0f, 0f) : Quaternion.Euler(0f, 0f, 45f);
                roof.position = ground + Vector3.up * height;
                break;
            }
            case AgeVisualSet.RoofStyle.Default when grown.Zone == ZoneType.Industrial:
            {
                // Chimney at the back corner, taller than the shed.
                float width = 0.14f * scale;
                float chimneyHeight = height + 0.45f * scale;
                float offset = footprint * 0.5f - width * 0.5f;
                roof.rotation = Quaternion.identity;
                roof.localScale = new Vector3(width, chimneyHeight, width);
                roof.position = ground + new Vector3(-offset, chimneyHeight * 0.5f, -offset);
                break;
            }
            default:
            {
                // Overhanging cap on houses; an inset rooftop box on taller buildings.
                float capFootprint = grown.Level == 1 ? footprint * 1.08f : footprint * 0.55f;
                float capHeight = (grown.Level == 1 ? 0.10f : 0.14f) * scale;
                roof.rotation = Quaternion.identity;
                roof.localScale = new Vector3(capFootprint, capHeight, capFootprint);
                roof.position = ground + Vector3.up * (height + capHeight * 0.5f);
                break;
            }
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

    // Deterministic per-cell variation (same city -> same skyline after a reload). The low 16 bits
    // drive the height jitter (unchanged from M10), higher bits the roof ridge and prefab picks.
    private static uint CellHash(Vector2Int cell)
    {
        uint h = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
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

    private PrefabVisual AcquirePrefab(GameObject prefab)
    {
        if (m_FreePrefabs.TryGetValue(prefab, out Stack<PrefabVisual> pool) && pool.Count > 0)
        {
            PrefabVisual pooled = pool.Pop();
            SetPrefabShown(pooled, true);
            return pooled;
        }

        GameObject instance = Instantiate(prefab, transform);
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        var originals = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++) originals[i] = renderers[i].sharedMaterials;
        return new PrefabVisual
        {
            Source = prefab,
            Transform = instance.transform,
            Renderers = renderers,
            Originals = originals,
            Colliders = instance.GetComponentsInChildren<Collider>(true),
        };
    }

    private void ReleasePrefab(Grown grown)
    {
        PrefabVisual visual = grown.Prefab;
        grown.Prefab = null;
        SetPrefabShown(visual, false);
        if (!m_FreePrefabs.TryGetValue(visual.Source, out Stack<PrefabVisual> pool))
        {
            pool = new Stack<PrefabVisual>();
            m_FreePrefabs.Add(visual.Source, pool);
        }
        pool.Push(visual);
    }

    private static void SetPrefabShown(PrefabVisual visual, bool shown)
    {
        foreach (Renderer renderer in visual.Renderers) renderer.enabled = shown;
        foreach (Collider collider in visual.Colliders) collider.enabled = shown;
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

    // Zone colours (blended per age), roof shades and the info views' tints are a small fixed set,
    // so this stays tiny.
    private Material MaterialFor(Color color)
    {
        color.a = 1f;
        if (m_Materials.TryGetValue(color, out Material material)) return material;

        material = m_Material != null ? new Material(m_Material) : new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
        material.name = $"Grown {ColorUtility.ToHtmlStringRGB(color)}";
        material.SetColor(s_BaseColorId, color);
        m_Materials.Add(color, material);
        return material;
    }
}
