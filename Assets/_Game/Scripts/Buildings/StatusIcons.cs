using System.Collections.Generic;
using UnityEngine;

// Status icons over buildings (M21c): a pooled world-space billboard sprite over every placed building with something
// wrong. Today one status, Repair (a broken-down plant or pump from DisasterSystem.Broken); a new status is one enum
// value, one sprite in MakeSprite and one line in Collect. All icons share one sprite material (no per-instance
// materials); the sprites are drawn in code. Presentation only: it reads the sim and never writes it. No colliders, so
// a click still reaches the building (SelectionPanel then offers the Repair button). The set is rescanned whenever a
// day's step has run and a few times a second otherwise.
public sealed class StatusIcons : MonoBehaviour
{
    public enum Status { Repair }

    private sealed class Icon
    {
        public Transform Root;
        public SpriteRenderer Renderer;
        public BuildingInstance Building;
        public Status Status;
        public float Phase;
        public float Top;
    }

    private const float RescanInterval = 0.25f;
    private const float IconSize = 0.9f;        // world units across

    private GameManager m_Game;
    private Material m_Material;
    private readonly Dictionary<Status, Sprite> m_Sprites = new();
    private readonly List<Icon> m_Active = new();
    private readonly List<Icon> m_Pool = new();
    private readonly List<(BuildingInstance building, Status status)> m_Wanted = new();
    private int m_LastSteps = -1;
    private float m_NextScan;
    private Camera m_Camera;

    public int ActiveCount => m_Active.Count;

    public void Init(GameManager game)
    {
        m_Game = game;
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader != null) m_Material = new Material(shader) { name = "StatusIconMaterial" };
        GameEvents.WorldResized += OnWorldResized;
    }

    private void OnDestroy()
    {
        GameEvents.WorldResized -= OnWorldResized;
        if (m_Material != null) Destroy(m_Material);
        foreach (Sprite sprite in m_Sprites.Values)
        {
            if (sprite == null) continue;
            if (sprite.texture != null) Destroy(sprite.texture);
            Destroy(sprite);
        }
    }

    private void OnWorldResized(Vector2Int size) => m_LastSteps = -1;

    private void LateUpdate()
    {
        SimulationSystem sim = m_Game != null ? m_Game.Simulation : null;
        if (sim == null || m_Material == null) return;

        int steps = sim.Disasters.Steps;
        if (steps != m_LastSteps || Time.unscaledTime >= m_NextScan)
        {
            m_LastSteps = steps;
            m_NextScan = Time.unscaledTime + RescanInterval;
            Rescan(sim);
        }

        if (m_Camera == null) m_Camera = Camera.main;
        float t = Time.unscaledTime;
        foreach (Icon icon in m_Active)
        {
            if (icon.Building == null) continue;
            if (m_Camera != null) icon.Root.rotation = m_Camera.transform.rotation;
            Vector3 position = icon.Building.transform.position;
            position.y = icon.Top + 0.5f + 0.08f * Mathf.Sin(t * 3f + icon.Phase);
            icon.Root.position = position;
        }
    }

    // Everything that needs an icon now: (placed building, status).
    private void Collect(SimulationSystem sim, List<(BuildingInstance, Status)> into)
    {
        into.Clear();
        if (!sim.Disasters.Breakdowns.AnyBroken) return;
        foreach (BuildingInstance building in m_Game.SourceBuildings)
        {
            if (building != null && sim.Disasters.Breakdowns.IsBroken(building.Origin)) into.Add((building, Status.Repair));
        }
    }

    // Reuses the icons already on a building and status, frees the rest, takes new ones from the pool.
    private void Rescan(SimulationSystem sim)
    {
        Collect(sim, m_Wanted);

        for (int i = m_Active.Count - 1; i >= 0; i--)
        {
            Icon icon = m_Active[i];
            int match = m_Wanted.FindIndex(w => w.building == icon.Building && w.status == icon.Status);
            if (match >= 0 && icon.Building != null)
            {
                m_Wanted.RemoveAt(match);
                continue;
            }
            icon.Root.gameObject.SetActive(false);
            icon.Building = null;
            m_Pool.Add(icon);
            m_Active.RemoveAt(i);
        }

        foreach ((BuildingInstance building, Status status) in m_Wanted)
        {
            Icon icon = Take();
            icon.Building = building;
            icon.Status = status;
            icon.Renderer.sprite = SpriteFor(status);
            icon.Phase = (building.Origin.x * 1.7f + building.Origin.y * 2.3f) % 6.28f;
            icon.Top = TopOf(building);
            icon.Root.gameObject.SetActive(true);
            m_Active.Add(icon);
        }
    }

    // The top of the building's visual (its renderers' bounds), so the icon floats above the roof.
    private static float TopOf(BuildingInstance building)
    {
        float top = building.transform.position.y + 1f;
        foreach (Renderer renderer in building.GetComponentsInChildren<Renderer>())
        {
            top = Mathf.Max(top, renderer.bounds.max.y);
        }
        return top;
    }

    private Icon Take()
    {
        if (m_Pool.Count > 0)
        {
            Icon pooled = m_Pool[m_Pool.Count - 1];
            m_Pool.RemoveAt(m_Pool.Count - 1);
            return pooled;
        }

        var root = new GameObject("StatusIcon");
        root.transform.SetParent(transform, false);
        root.transform.localScale = Vector3.one * IconSize;
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = m_Material;
        renderer.sortingOrder = 100;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return new Icon { Root = root.transform, Renderer = renderer };
    }

    private Sprite SpriteFor(Status status)
    {
        if (!m_Sprites.TryGetValue(status, out Sprite sprite))
        {
            sprite = MakeSprite(status);
            m_Sprites[status] = sprite;
        }
        return sprite;
    }

    // Repair: a red badge with a white spanner (a bar at 45 degrees with an open head).
    private static Sprite MakeSprite(Status status)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "StatusIcon_" + status,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        Color badge = new Color(0.86f, 0.22f, 0.18f, 1f);
        Color rim = new Color(1f, 1f, 1f, 1f);
        float centre = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre, dy = y - centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float coverage = Mathf.Clamp01(centre - distance + 0.5f);       // the disc, anti-aliased
                Color colour = distance > centre - 3.5f ? rim : badge;

                // Spanner: shaft along the diagonal, a ring at the upper right with a notch cut into it.
                float along = (dx - dy) * 0.7071f, across = (dx + dy) * 0.7071f;
                bool shaft = Mathf.Abs(across) < 4.2f && along > -17f && along < 12f;
                float headDistance = Mathf.Sqrt((along - 15f) * (along - 15f) + across * across);
                bool head = headDistance < 11f && !(headDistance < 6.5f) && !(along > 14f && Mathf.Abs(across) < 5f);
                if (shaft || head) colour = rim;
                pixels[y * size + x] = new Color(colour.r, colour.g, colour.b, coverage);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
