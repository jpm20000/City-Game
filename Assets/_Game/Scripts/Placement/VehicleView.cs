using System.Collections.Generic;
using UnityEngine;

// Cosmetic traffic (M18f): small carts, trams and cars drive along the roads in view, as many as the traffic
// flow says the roads carry (density = load / capacity). Visual only: it reads TrafficSystem and never writes
// the sim or touches SimRandom. Pooled prefabs on the shared Kit material (one renderer each, no collider),
// capped (fewer when zoomed out), frozen while the game is paused, hidden under an info view, and its
// headlights glow with the day/night cycle (emissive swatches of the Kit material).
public sealed class VehicleView : MonoBehaviour
{
    private const int k_MaxVehicles = 250;
    private const float k_PerFullRoad = 1.5f;       // vehicles on a road cell at 100% load / capacity
    private const float k_BaseSpeed = 3.2f;         // cells / s = this / the tier's travel cost
    private const float k_LaneOffset = 0.17f;       // cells to the right of the centre line
    private const float k_Scale = 1.5f;             // the kit's vehicles are drawn small; a street cell reads better with these
    private const float k_RefreshSeconds = 0.5f;
    private const int k_SpawnsPerRefresh = 12;
    private const float k_ViewMargin = 2f;

    private sealed class Vehicle
    {
        public Transform Transform;
        public Renderer Renderer;
        public GameObject Source;
        public Vector2Int From, To;
        public float Progress;
        public Vector3 Offset;
    }

    private GameManager m_Game;
    private VehicleSet m_Set;
    private InfoOverlay m_Overlay;
    private Camera m_Camera;
    private readonly List<Vehicle> m_Active = new();
    private readonly Dictionary<GameObject, Stack<Vehicle>> m_Pool = new();
    private readonly List<Vector2Int> m_ViewRoads = new();
    private readonly List<float> m_ViewWeights = new();
    private readonly float[] m_Weights = new float[4];
    private float m_NextRefresh;
    private int m_Seed = 12345;

    public int ActiveCount => m_Active.Count;

    public void Init(GameManager game, VehicleSet set, InfoOverlay overlay)
    {
        m_Game = game;
        m_Set = set;
        m_Overlay = overlay;
        GameEvents.CityLoaded += ReleaseAll;
        GameEvents.WorldResized += OnWorldResized;
    }

    private void OnDestroy()
    {
        GameEvents.CityLoaded -= ReleaseAll;
        GameEvents.WorldResized -= OnWorldResized;
    }

    private void OnWorldResized(Vector2Int size) => ReleaseAll();

    private void Update()
    {
        if (m_Game == null || m_Game.Simulation == null || m_Set == null) return;

        // Under an info view the street is part of the picture: no traffic on top of it.
        if (m_Overlay != null && m_Overlay.Shown != InfoOverlay.View.Off)
        {
            if (m_Active.Count > 0) ReleaseAll();
            return;
        }

        if (Time.unscaledTime >= m_NextRefresh)
        {
            m_NextRefresh = Time.unscaledTime + k_RefreshSeconds;
            Refresh();
        }
        Move(Time.deltaTime * SpeedMultiplier());
    }

    private float SpeedMultiplier()
    {
        switch (m_Game.Clock.Speed)
        {
            case GameSpeed.x1: return 1f;
            case GameSpeed.x2: return 2f;
            case GameSpeed.x4: return 3f;
            default: return 0f;
        }
    }

    // ---- which roads are in view, how many vehicles they carry -------------------------------------------

    private void Refresh()
    {
        GridData grid = m_Game.Grid;
        TrafficSystem traffic = m_Game.Simulation.Traffic;
        if (m_Camera == null) m_Camera = Camera.main;
        if (grid == null || traffic == null || m_Camera == null) return;

        if (!ViewRect(grid, k_ViewMargin, out int x0, out int y0, out int x1, out int y1)) return;

        m_ViewRoads.Clear();
        m_ViewWeights.Clear();
        float total = 0f;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsRoad(cell)) continue;
                float w = traffic.Congestion(cell);
                if (w <= 0.02f) continue;
                m_ViewRoads.Add(cell);
                m_ViewWeights.Add(w);
                total += w;
            }
        }

        // The cap shrinks as the camera pulls back (tiny dots across a whole map are not worth the cost).
        float zoom = m_Camera.orthographicSize;
        int cap = Mathf.RoundToInt(Mathf.Lerp(k_MaxVehicles, k_MaxVehicles * 0.25f, Mathf.InverseLerp(20f, 45f, zoom)));
        int target = Mathf.Min(cap, Mathf.RoundToInt(total * k_PerFullRoad));

        // Out of view, off a road that is gone, or too many: back to the pool.
        for (int i = m_Active.Count - 1; i >= 0; i--)
        {
            Vehicle v = m_Active[i];
            bool outside = v.From.x < x0 || v.From.x > x1 || v.From.y < y0 || v.From.y > y1;
            if (outside || !grid.IsRoad(v.From) || !grid.IsRoad(v.To) || (m_Active.Count > target * 1.25f + 2 && NextInt(4) == 0))
            {
                Release(i);
            }
        }

        for (int spawned = 0; m_Active.Count < target && spawned < k_SpawnsPerRefresh && total > 0f; spawned++)
        {
            Spawn(grid, total);
        }
    }

    // The camera's corners on the ground plane, as a cell rectangle clamped to the map.
    private bool ViewRect(GridData grid, float margin, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = x1 = y1 = 0;
        var ground = new Plane(Vector3.up, Vector3.zero);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < 4; c++)
        {
            Ray ray = m_Camera.ViewportPointToRay(new Vector2(c & 1, c >> 1));
            if (!ground.Raycast(ray, out float t)) continue;
            Vector3 p = ray.GetPoint(t);
            min = Vector2.Min(min, new Vector2(p.x, p.z));
            max = Vector2.Max(max, new Vector2(p.x, p.z));
        }
        if (min.x > max.x) return false;
        x0 = Mathf.Max(0, Mathf.FloorToInt(min.x - margin));
        y0 = Mathf.Max(0, Mathf.FloorToInt(min.y - margin));
        x1 = Mathf.Min(grid.Width - 1, Mathf.CeilToInt(max.x + margin));
        y1 = Mathf.Min(grid.Height - 1, Mathf.CeilToInt(max.y + margin));
        return x0 <= x1 && y0 <= y1;
    }

    private void Spawn(GridData grid, float total)
    {
        float pick = Next01() * total;
        int index = 0;
        for (; index < m_ViewRoads.Count - 1; index++)
        {
            pick -= m_ViewWeights[index];
            if (pick <= 0f) break;
        }
        Vector2Int from = m_ViewRoads[index];
        Vector2Int to = default;
        int found = 0;
        foreach (Vector2Int step in CellUtils.Neighbors4)
        {
            Vector2Int next = from + step;
            if (grid.InBounds(next) && grid.IsRoad(next) && NextInt(++found) == 0) to = next;
        }
        if (found == 0) return;

        GameObject[] prefabs = m_Set.ForAge(CurrentAge());
        if (prefabs.Length == 0) return;
        Vehicle vehicle = Acquire(prefabs[NextInt(prefabs.Length)]);
        vehicle.From = from;
        vehicle.To = to;
        vehicle.Progress = Next01();
        Place(vehicle, true);
        m_Active.Add(vehicle);
    }

    private int CurrentAge()
    {
        SimulationSystem sim = m_Game.Simulation;
        return sim.Tech != null ? sim.Tech.CurrentAge : 2;
    }

    // ---- driving ---------------------------------------------------------------------------------------

    private void Move(float dt)
    {
        if (dt <= 0f) return;
        GridData grid = m_Game.Grid;
        TrafficSystem traffic = m_Game.Simulation.Traffic;
        RoadTiers tiers = m_Game.Simulation.RoadTiers;

        for (int i = m_Active.Count - 1; i >= 0; i--)
        {
            Vehicle v = m_Active[i];
            int tier = grid.GetRoadTier(v.From);
            float cost = tier > 0 ? Mathf.Max(1, tiers.TravelCost(tier)) : 4f;
            v.Progress += k_BaseSpeed / cost * dt;
            bool gone = false;
            while (v.Progress >= 1f)
            {
                v.Progress -= 1f;
                Vector2Int previous = v.From;
                v.From = v.To;
                if (!TryChooseNext(grid, traffic, previous, v.From, out Vector2Int next))
                {
                    gone = true;
                    break;
                }
                v.To = next;
            }
            if (gone) Release(i);
            else Place(v, false);
        }
    }

    // Straight on is preferred, busy roads attract; a dead end (or the map edge) ends the trip.
    private bool TryChooseNext(GridData grid, TrafficSystem traffic, Vector2Int previous, Vector2Int at, out Vector2Int next)
    {
        next = default;
        float total = 0f;
        Vector2Int heading = at - previous;
        for (int s = 0; s < 4; s++)
        {
            m_Weights[s] = 0f;
            Vector2Int candidate = at + CellUtils.Neighbors4[s];
            if (candidate == previous || !grid.InBounds(candidate) || !grid.IsRoad(candidate)) continue;
            float w = 0.25f + traffic.Congestion(candidate);
            if (CellUtils.Neighbors4[s] == heading) w *= 2f;
            m_Weights[s] = w;
            total += w;
        }
        if (total <= 0f) return false;

        float pick = Next01() * total;
        for (int s = 0; s < 4; s++)
        {
            if (m_Weights[s] <= 0f) continue;
            pick -= m_Weights[s];
            next = at + CellUtils.Neighbors4[s];
            if (pick <= 0f) break;
        }
        return true;
    }

    // Right-hand traffic: the lane sits to the right of the direction of travel.
    private static Vector3 Right(Vector3 forward) => new Vector3(forward.z, 0f, -forward.x);

    private void Place(Vehicle v, bool snap)
    {
        Vector3 a = new Vector3(v.From.x + 0.5f, 0f, v.From.y + 0.5f);
        Vector3 b = new Vector3(v.To.x + 0.5f, 0f, v.To.y + 0.5f);
        Vector3 forward = (b - a).normalized;
        Vector3 wanted = Right(forward) * k_LaneOffset;
        v.Offset = snap ? wanted : Vector3.MoveTowards(v.Offset, wanted, 0.9f * Time.deltaTime);
        Vector3 position = Vector3.Lerp(a, b, v.Progress) + v.Offset;
        Quaternion rotation = Quaternion.LookRotation(forward);
        v.Transform.SetPositionAndRotation(position, snap ? rotation : Quaternion.RotateTowards(v.Transform.rotation, rotation, 540f * Time.deltaTime));
    }

    // ---- pool --------------------------------------------------------------------------------------------

    private Vehicle Acquire(GameObject prefab)
    {
        if (m_Pool.TryGetValue(prefab, out Stack<Vehicle> stack) && stack.Count > 0)
        {
            Vehicle pooled = stack.Pop();
            pooled.Renderer.enabled = true;
            return pooled;
        }
        GameObject instance = Instantiate(prefab, transform);
        instance.transform.localScale = Vector3.one * k_Scale;
        return new Vehicle { Transform = instance.transform, Renderer = instance.GetComponentInChildren<Renderer>(), Source = prefab };
    }

    private void Release(int index)
    {
        Vehicle v = m_Active[index];
        m_Active.RemoveAt(index);
        v.Renderer.enabled = false;
        if (!m_Pool.TryGetValue(v.Source, out Stack<Vehicle> stack))
        {
            stack = new Stack<Vehicle>();
            m_Pool.Add(v.Source, stack);
        }
        stack.Push(v);
    }

    public void ReleaseAll()
    {
        for (int i = m_Active.Count - 1; i >= 0; i--) Release(i);
    }

    // A tiny private generator: cosmetic randomness must not share state with anything in the sim.
    private float Next01()
    {
        m_Seed = unchecked(m_Seed * 1103515245 + 12345);
        return ((m_Seed >> 8) & 0xFFFFFF) / (float)0x1000000;
    }

    private int NextInt(int n) => Mathf.Min(n - 1, (int)(Next01() * n));
}
