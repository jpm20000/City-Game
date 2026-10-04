using UnityEngine;
using UnityEngine.UI;

// Developer panel toggled with F1 (DebugToggle). Only available in the Editor and development builds.
public sealed class DebugPanel : MonoBehaviour
{
    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GameObject m_Root;
    [SerializeField] private Button m_SeedCityButton;
    [SerializeField] private Button m_AddMoneyButton;
    [SerializeField] private Button m_SkipDaysButton;
    [SerializeField] private float m_MoneyAmount = 10000f;
    [SerializeField] private int m_SkipDays = 30;

    private void Start()
    {
        m_Root.SetActive(false);
        if (!Debug.isDebugBuild)
        {
            enabled = false;
            return;
        }

        Bind(m_SeedCityButton, () => m_Placement.DebugSeedCity());
        Bind(m_AddMoneyButton, () => m_GameManager.Economy.Refund(m_MoneyAmount));
        Bind(m_SkipDaysButton, () => m_GameManager.Clock.DebugAdvanceDays(m_SkipDays));

        // M17: start each hazard by hand (each switches disasters on first). Copies of the Skip button.
        AddDebugButton("Fire at selection", () => { Disasters().Fire.Ignite(SelectedOrCentre()); });
        AddDebugButton("Start plague", StartPlague);
        AddDebugButton("Break a plant", BreakAPlant);
        AddDebugButton("Offer event", () => { Disasters().Events.OfferNow(); });
    }

    private DisasterSystem Disasters()
    {
        DisasterSystem d = m_GameManager.Simulation.Disasters;
        d.Enabled = true;
        return d;
    }

    private Vector2Int SelectedOrCentre()
    {
        if (m_Placement.HasSelection) return m_Placement.SelectedCell;
        return new Vector2Int(m_GameManager.MapSize.x / 2, m_GameManager.MapSize.y / 2);
    }

    // Infects the selected home, else the first grown home in row-major order.
    private void StartPlague()
    {
        DisasterSystem d = Disasters();
        GridData grid = m_GameManager.Grid;
        if (m_Placement.HasSelection && d.Epidemic.Infect(m_Placement.SelectedCell)) return;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (d.Epidemic.Infect(new Vector2Int(x, y))) return;
            }
        }
    }

    // Breaks the selected plant / tower / pump, else the first one placed.
    private void BreakAPlant()
    {
        DisasterSystem d = Disasters();
        if (m_Placement.HasSelection)
        {
            BuildingInstance selected = m_Placement.GetBuildingAt(m_Placement.SelectedCell);
            if (selected != null && d.Breakdowns.Break(selected.Origin)) return;
        }
        foreach (ServiceSource source in m_GameManager.Simulation.Sources)
        {
            if (BreakdownSystem.CanBreak(source) && d.Breakdowns.Break(source.Origin)) return;
        }
    }

    private void AddDebugButton(string label, UnityEngine.Events.UnityAction action)
    {
        if (m_SkipDaysButton == null) return;
        Button copy = Instantiate(m_SkipDaysButton, m_SkipDaysButton.transform.parent);
        copy.name = label.Replace(" ", "") + "Button";
        copy.onClick.RemoveAllListeners();
        copy.onClick.AddListener(action);
        var text = copy.GetComponentInChildren<TMPro.TMP_Text>();
        if (text != null) text.text = label;
    }

    private void Update()
    {
        if (m_InputReader != null && m_InputReader.DebugTogglePressed)
        {
            m_Root.SetActive(!m_Root.activeSelf);
        }
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }
}
