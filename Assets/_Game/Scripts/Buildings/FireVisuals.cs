using System.Collections.Generic;
using UnityEngine;

// Flames and smoke over burning cells and a bobbing green marker over infected homes (M17): pooled cubes, sharing two materials (no per-instance
// materials), animated in Update. The burning set is rescanned from DisasterSystem.Fires whenever a day's step has
// run and a few times a second otherwise (a DEBUG fire lit between ticks shows up at once). No colliders, so clicks
// still reach the building under the flames.
public sealed class FireVisuals : MonoBehaviour
{
    private sealed class Flame
    {
        public Transform Root;
        public Transform Fire;
        public Transform Smoke;
        public Vector2Int Cell;
        public float Phase;
        public bool Sick;           // a plague marker (only the Smoke cube, in the sick material)
    }

    private const float RescanInterval = 0.25f;

    private GameManager m_Game;
    private GridSystem m_GridSystem;
    private Material m_FireMaterial;
    private Material m_SmokeMaterial;
    private Material m_SickMaterial;
    private readonly List<Flame> m_Active = new();
    private readonly List<Flame> m_Pool = new();
    private int m_LastSteps = -1;
    private float m_NextScan;

    public void Init(GameManager game, GridSystem gridSystem)
    {
        m_Game = game;
        m_GridSystem = gridSystem;
        m_FireMaterial = MakeMaterial("FlameMaterial", new Color(1f, 0.42f, 0.08f));
        m_SmokeMaterial = MakeMaterial("SmokeMaterial", new Color(0.22f, 0.22f, 0.25f));
        m_SickMaterial = MakeMaterial("SickMaterial", new Color(0.72f, 0.85f, 0.10f));
        GameEvents.WorldResized += OnWorldResized;
    }

    private void OnDestroy()
    {
        GameEvents.WorldResized -= OnWorldResized;
        if (m_FireMaterial != null) Destroy(m_FireMaterial);
        if (m_SmokeMaterial != null) Destroy(m_SmokeMaterial);
        if (m_SickMaterial != null) Destroy(m_SickMaterial);
    }

    private static Material MakeMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0f);
        return material;
    }

    private void OnWorldResized(Vector2Int size)
    {
        ReleaseAll();
        m_LastSteps = -1;
    }

    private void ReleaseAll()
    {
        foreach (Flame flame in m_Active)
        {
            flame.Root.gameObject.SetActive(false);
            m_Pool.Add(flame);
        }
        m_Active.Clear();
    }

    private void Update()
    {
        SimulationSystem sim = m_Game.Simulation;
        if (sim == null || m_Game.Grid == null) return;

        int steps = sim.Disasters.Steps;
        if (steps != m_LastSteps || Time.unscaledTime >= m_NextScan)
        {
            m_LastSteps = steps;
            m_NextScan = Time.unscaledTime + RescanInterval;
            Rescan(sim.Disasters.Fires, sim.Disasters.Plague);
        }

        float t = Time.time;
        foreach (Flame flame in m_Active)
        {
            if (flame.Sick)
            {
                flame.Smoke.localPosition = new Vector3(0f, 0.15f + 0.12f * Mathf.Sin(t * 3f + flame.Phase), 0f);
                flame.Smoke.localScale = new Vector3(0.28f, 0.28f, 0.28f);
                flame.Smoke.localRotation = Quaternion.Euler(0f, t * 60f, 0f);
                continue;
            }
            float wobble = 0.75f + 0.25f * Mathf.Sin(t * 9f + flame.Phase);
            flame.Fire.localScale = new Vector3(0.45f * (1.1f - 0.2f * wobble), 0.55f * wobble + 0.25f, 0.45f * (1.1f - 0.2f * wobble));
            flame.Fire.localRotation = Quaternion.Euler(0f, 45f + 20f * Mathf.Sin(t * 3f + flame.Phase), 0f);
            float rise = Mathf.Repeat(t * 0.5f + flame.Phase, 1f);
            flame.Smoke.localPosition = new Vector3(0f, 0.55f + rise * 0.9f, 0f);
            float size = 0.2f + 0.25f * rise;
            flame.Smoke.localScale = new Vector3(size, size, size);
        }
    }

    // Matches the pooled flames to the burning cells (row-major): reuse the ones already on a cell, then take from the pool.
    private void Rescan(byte[] fires, byte[] plague)
    {
        GridData grid = m_Game.Grid;
        int count = grid.Width * grid.Height;
        if (fires.Length != count) return;

        // Keys: a burning cell's index, or index + count for an infected home that is not burning.
        var wanted = new HashSet<int>();
        for (int i = 0; i < fires.Length; i++)
        {
            if (fires[i] > 0) wanted.Add(i);
            else if (plague[i] != 0 && plague[i] != DisasterSystem.PlagueRecovered) wanted.Add(i + count);
        }

        for (int i = m_Active.Count - 1; i >= 0; i--)
        {
            Flame flame = m_Active[i];
            int index = flame.Cell.y * grid.Width + flame.Cell.x + (flame.Sick ? count : 0);
            if (wanted.Remove(index)) continue;
            flame.Root.gameObject.SetActive(false);
            m_Pool.Add(flame);
            m_Active.RemoveAt(i);
        }

        foreach (int key in wanted)
        {
            bool sick = key >= count;
            int index = sick ? key - count : key;
            var cell = new Vector2Int(index % grid.Width, index / grid.Width);
            Flame flame = Take();
            flame.Cell = cell;
            flame.Sick = sick;
            flame.Fire.gameObject.SetActive(!sick);
            flame.Smoke.GetComponent<MeshRenderer>().sharedMaterial = sick ? m_SickMaterial : m_SmokeMaterial;
            flame.Phase = (cell.x * 1.7f + cell.y * 2.3f) % 6.28f;
            Vector3 world = m_GridSystem.CellToWorld(cell);
            int level = grid.GetBuildingLevel(cell);
            world.y = level > 0 ? 0.35f + 0.3f * level : 0.45f;
            if (sick) world.y += 0.35f;
            flame.Root.position = world;
            flame.Root.gameObject.SetActive(true);
            m_Active.Add(flame);
        }
    }

    private Flame Take()
    {
        if (m_Pool.Count > 0)
        {
            Flame pooled = m_Pool[m_Pool.Count - 1];
            m_Pool.RemoveAt(m_Pool.Count - 1);
            return pooled;
        }

        var root = new GameObject("Flame");
        root.transform.SetParent(transform, false);
        return new Flame
        {
            Root = root.transform,
            Fire = MakeCube("Fire", root.transform, m_FireMaterial),
            Smoke = MakeCube("Smoke", root.transform, m_SmokeMaterial),
        };
    }

    private static Transform MakeCube(string name, Transform parent, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(parent, false);
        var renderer = cube.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return cube.transform;
    }
}
