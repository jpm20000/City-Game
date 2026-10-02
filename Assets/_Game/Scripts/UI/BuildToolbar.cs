using UnityEngine;

public sealed class BuildToolbar : MonoBehaviour
{
    private static readonly Rect s_Area = new Rect(10f, 10f, 440f, 280f);

    private PlacementController m_Placement;
    private GameManager m_GameManager;

    private void Awake()
    {
        m_Placement = FindAnyObjectByType<PlacementController>();
        m_GameManager = FindAnyObjectByType<GameManager>();
    }

    private void OnGUI()
    {
        if (m_Placement == null || m_GameManager == null || m_GameManager.Buildings == null)
        {
            return;
        }

        if (Event.current.type == EventType.Repaint)
        {
            m_Placement.PointerOverUI = s_Area.Contains(Event.current.mousePosition);
        }

        GUILayout.BeginArea(s_Area);

        GUILayout.BeginHorizontal();
        ToolButton("Road", m_Placement.CurrentMode == PlacementController.Mode.Road, m_Placement.SelectRoad);
        ToolButton("Park", m_Placement.CurrentMode == PlacementController.Mode.Building,
            () => m_Placement.SelectBuilding(m_GameManager.Buildings.GetById("park")));
        ToolButton("Demolish", m_Placement.CurrentMode == PlacementController.Mode.Demolish, m_Placement.SelectDemolish);
        if (GUILayout.Button("Cancel"))
        {
            m_Placement.ClearMode();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        ZoneButton("Residential", ZoneType.Residential);
        ZoneButton("Commercial", ZoneType.Commercial);
        ZoneButton("Industrial", ZoneType.Industrial);
        ZoneButton("Unzone", ZoneType.None);
        GUILayout.EndHorizontal();

        DrawSimulationDebug();

        GUILayout.EndArea();
    }

    private void ZoneButton(string label, ZoneType zone)
    {
        bool active = m_Placement.CurrentMode == PlacementController.Mode.Zone && m_Placement.ZoneBrush == zone;
        ToolButton(label, active, () => m_Placement.SelectZone(zone));
    }

    // Active tool is drawn disabled so the current selection is visible.
    private static void ToolButton(string label, bool active, System.Action onClick)
    {
        GUI.enabled = !active;
        if (GUILayout.Button(label))
        {
            onClick();
        }
        GUI.enabled = true;
    }

    // M6 debug readout; replaced by the GameEvents-driven HUD in M7.
    private void DrawSimulationDebug()
    {
        TimeManager clock = m_GameManager.Clock;
        EconomySystem economy = m_GameManager.Economy;
        PopulationSystem population = m_GameManager.Population;
        DemandSystem demand = m_GameManager.Demand;
        if (clock == null || economy == null) return;

        GUILayout.BeginHorizontal();
        ToolButton("||", clock.Speed == GameSpeed.Paused, () => clock.SetSpeed(GameSpeed.Paused));
        ToolButton("1x", clock.Speed == GameSpeed.x1, () => clock.SetSpeed(GameSpeed.x1));
        ToolButton("2x", clock.Speed == GameSpeed.x2, () => clock.SetSpeed(GameSpeed.x2));
        ToolButton("4x", clock.Speed == GameSpeed.x4, () => clock.SetSpeed(GameSpeed.x4));
        if (GUILayout.Button("Seed test city"))
        {
            m_Placement.DebugSeedCity();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label($"Day {clock.Day}  Month {clock.Month}  Year {clock.Year}   [{clock.Speed}]");
        GUILayout.Label($"Money ${economy.Money:N0}   (+{economy.IncomePerDay:N1} / -{economy.ExpensePerDay:N1} per day)");
        GUILayout.Label($"Population {population.Population} / {population.Housing} housing   Homeless {population.Homeless}");
        GUILayout.Label($"Jobs {population.Jobs} (C {population.CommercialJobs} / I {population.IndustrialJobs})   Employed {population.Employed} / {population.Workers} workers");
        GUILayout.Label($"Happiness {population.AverageHappiness:P0}");
        GUILayout.Label($"Demand  R {demand.ResidentialDemand:F2}   C {demand.CommercialDemand:F2}   I {demand.IndustrialDemand:F2}");
        GUILayout.EndVertical();
    }
}
