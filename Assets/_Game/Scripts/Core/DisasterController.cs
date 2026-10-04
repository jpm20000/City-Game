using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Ties the M17 disasters to the scene (built at runtime, so no scene edit): removes the placed buildings that burnt
// down (the sim has already released their cells and dropped their sources), toasts each fire / outbreak / breakdown
// / event result, and creates the ground view (HazardView), the flames (FireVisuals) and the event popup beside it.
public sealed class DisasterController : MonoBehaviour
{
    private GameManager m_Game;
    private PlacementController m_Placement;
    private int m_LastSteps;
    private bool m_Burning;
    private int m_LostBlocks;
    private readonly List<string> m_LostBuildings = new();
    private bool m_FireHintShown;
    private bool m_PlagueHintShown;
    private bool m_BreakdownHintShown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameManager game = FindAnyObjectByType<GameManager>();
        PlacementController placement = FindAnyObjectByType<PlacementController>();
        GridSystem gridSystem = FindAnyObjectByType<GridSystem>();
        if (game == null || placement == null || gridSystem == null) return;

        Tilemap roads = null;
        foreach (Tilemap tilemap in FindObjectsByType<Tilemap>(FindObjectsInactive.Include))
        {
            if (tilemap.name == "Roads") roads = tilemap;
        }

        var go = new GameObject("Disasters");
        var controller = go.AddComponent<DisasterController>();
        controller.m_Game = game;
        controller.m_Placement = placement;

        if (roads != null)
        {
            var view = new GameObject("HazardView").AddComponent<HazardView>();
            view.transform.SetParent(go.transform, false);
            view.Init(game, gridSystem, roads);
        }
        var fire = new GameObject("FireVisuals").AddComponent<FireVisuals>();
        fire.transform.SetParent(go.transform, false);
        fire.Init(game, gridSystem);

        NotificationController notifications = FindAnyObjectByType<NotificationController>(FindObjectsInactive.Include);
        if (notifications != null)
        {
            var popup = new GameObject("EventPopup", typeof(RectTransform)).AddComponent<EventPopup>();
            popup.Init(game, notifications.transform.root);
        }
    }

    private void Start()
    {
        SimulationSystem sim = m_Game.Simulation;
        if (sim == null) return;
        sim.BuildingsDestroyed += OnBuildingsDestroyed;
        sim.Disasters.Events.Resolved += OnEventResolved;
        m_LastSteps = sim.Disasters.Steps;
        GameEvents.CityLoaded += OnCityLoaded;
    }

    private void OnDestroy()
    {
        GameEvents.CityLoaded -= OnCityLoaded;
        SimulationSystem sim = m_Game != null ? m_Game.Simulation : null;
        if (sim == null) return;
        sim.BuildingsDestroyed -= OnBuildingsDestroyed;
        sim.Disasters.Events.Resolved -= OnEventResolved;
    }

    // A load / new city is not news: forget the old city's fire and start the hints again.
    private void OnCityLoaded()
    {
        m_LastSteps = m_Game.Simulation.Disasters.Steps;
        m_Burning = m_Game.Simulation.Disasters.Fire.BurningCount > 0;
        m_LostBlocks = 0;
        m_LostBuildings.Clear();
        m_FireHintShown = m_PlagueHintShown = m_BreakdownHintShown = false;
    }

    // The sim released the cells and dropped the sources; this removes the objects and their records (no refund).
    private void OnBuildingsDestroyed(IReadOnlyList<int> occupants)
    {
        var instances = new List<BuildingInstance>(m_Placement.PlacedBuildings);
        foreach (int occupant in occupants)
        {
            foreach (BuildingInstance instance in instances)
            {
                if (instance.OccupantId != occupant) continue;
                m_LostBuildings.Add(instance.Definition.DisplayName);
                break;
            }
            m_Placement.RemoveBurnt(occupant);
        }
    }

    private void Update()
    {
        SimulationSystem sim = m_Game.Simulation;
        if (sim == null || sim.Disasters.Steps == m_LastSteps) return;
        m_LastSteps = sim.Disasters.Steps;
        Report(sim.Disasters);
    }

    private void Report(DisasterSystem d)
    {
        FireSystem fire = d.Fire;
        if (!m_Burning && fire.BurningCount > 0)
        {
            m_Burning = true;
            m_LostBlocks = 0;
            m_LostBuildings.Clear();
            Toast(m_FireHintShown
                ? "<color=#FF7A29>Fire!</color> A block is burning."
                : "<color=#FF7A29>Fire!</color> A block is burning and may spread. Fire stations and water put fires out; roads slow them. [V] Fire view shows the cover.");
            m_FireHintShown = true;
            AudioController.Play(SfxId.FireStart);
        }
        m_LostBlocks += fire.LostBlocks;
        if (m_Burning && fire.BurningCount == 0)
        {
            m_Burning = false;
            string lost = m_LostBlocks == 0 && m_LostBuildings.Count == 0
                ? "nothing was lost"
                : $"{m_LostBlocks} block{(m_LostBlocks == 1 ? "" : "s")} burnt down{(m_LostBuildings.Count > 0 ? ", with the " + string.Join(", ", m_LostBuildings) : "")}";
            Toast($"The fire is out — {lost}.");
        }

        PlagueSystem plague = d.Epidemic;
        if (plague.Started)
        {
            Toast(m_PlagueHintShown
                ? "<color=#8FD14F>Plague</color> has broken out."
                : "<color=#8FD14F>Plague</color> has broken out — homes without health care catch it and residents die. Apothecaries and a quarantine slow it.");
            m_PlagueHintShown = true;
            AudioController.Play(SfxId.PlagueBell);
        }
        if (plague.Ended) Toast($"The plague is over — {d.PlagueDeaths} resident{(d.PlagueDeaths == 1 ? "" : "s")} died.");

        BreakdownSystem breakdowns = d.Breakdowns;
        if (breakdowns.Broke)
        {
            string name = NameAt(breakdowns.BrokeOrigin);
            float cost = m_Game.Simulation.RepairCost(breakdowns.BrokeOrigin);
            Toast($"<color=#F2665A>The {name} has broken down</color> — select it to repair it (${cost:N0}) or wait {m_Game.Balance.BreakdownDays} days."
                + (m_BreakdownHintShown ? "" : " Older and underfunded plants break down more often."));
            m_BreakdownHintShown = true;
            AudioController.Play(SfxId.Breakdown);
        }
    }

    private string NameAt(Vector2Int origin)
    {
        foreach (BuildingInstance instance in m_Placement.PlacedBuildings)
        {
            if (instance.Origin == origin) return instance.Definition.DisplayName;
        }
        return "building";
    }

    private void OnEventResolved(EventDefinition definition, int choiceIndex)
    {
        EventChoice choice = definition.Choices[choiceIndex];
        RandomEventSystem events = m_Game.Simulation.Disasters.Events;
        float price = choice.PriceAt(m_Game.Population.Population);
        string money = price > 0f ? $" (−${price:N0})" : choice.Reward > 0f ? $" (+${choice.Reward:N0})" : "";
        string how = events.LastWasAutomatic ? " — no answer, so the default was taken" : "";
        Toast($"<b>{definition.Title}</b>: {choice.Label}{money}{how}.");
    }

    private static void Toast(string message) => GameEvents.RaiseNotification(message);
}
