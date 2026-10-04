using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Short fading toasts (e.g. "Not enough money", GameEvents.Notification) plus a persistent banner while money is negative.
// Uses unscaled time so toasts still fade while the game is paused.
public sealed class NotificationController : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;

    [Header("Toast")]
    [SerializeField] private CanvasGroup m_Toast;
    [SerializeField] private TMP_Text m_ToastText;
    [SerializeField] private float m_ToastDuration = 2.5f;
    [SerializeField] private float m_FadeDuration = 0.4f;

    [Header("City events")]
    [SerializeField] private int[] m_PopulationMilestones = { 50, 100, 250, 500, 1000, 2000 };
    [Tooltip("(M13) Population at which a well-age city with dry buildings gets the 'dig a well' hint (wells gate the first upgrade, so earlier than the power hint).")]
    [SerializeField] private int m_WellNudgePopulation = 10;

    [Header("Debt banner")]
    [SerializeField] private GameObject m_Banner;
    [SerializeField] private TMP_Text m_BannerText;

    private float m_ToastTimer;
    private int m_MilestoneIndex;     // next milestone to announce
    private bool m_WasUnhappy;
    private bool m_HadPower;          // a connected plant was supplying power
    private bool m_WasShort;          // some grown buildings were unpowered despite a plant
    private bool m_NudgedNoPower;     // "build a power plant" hint already shown
    private bool m_HadWater;          // M13: a tower / pump was feeding the piped network
    private bool m_WasDry;            // M13: some grown buildings were dry despite a tower
    private bool m_NudgedNoWater;     // M13: "dig a well" / "build a water tower" hint already shown
    private bool m_AnnouncedReady;    // "ready to advance" shown for the current age
    private bool m_AnnouncedRebuild;  // first redevelopment of the current age shown
    private bool m_AnnouncedTraffic;   // first time commutes cost 1% happiness shown (M16)
    private bool m_AnnouncedLandValue; // first home / shop held at level 2 by land value shown (M12)
    private readonly bool[] m_AnnouncedCivic = new bool[5];   // [ServiceKind]: first time the line's need cost 2% shown (M14)

    private const float CivicToastThreshold = 0.02f;

    private void OnEnable()
    {
        GameEvents.InsufficientFunds += OnInsufficientFunds;
        GameEvents.MoneyChanged += OnMoneyChanged;
        GameEvents.Notification += ShowToast;
        GameEvents.PopulationChanged += OnPopulationChanged;
        GameEvents.HappinessChanged += OnHappinessChanged;
        GameEvents.CityLoaded += SyncCityState;
        GameEvents.PowerChanged += OnPowerChanged;
        GameEvents.WaterChanged += OnWaterChanged;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.AgeChanged += OnAgeChanged;
        GameEvents.ResearchChanged += OnResearchChanged;
        GameEvents.Redeveloped += OnRedeveloped;
    }

    private void OnDisable()
    {
        GameEvents.InsufficientFunds -= OnInsufficientFunds;
        GameEvents.MoneyChanged -= OnMoneyChanged;
        GameEvents.Notification -= ShowToast;
        GameEvents.PopulationChanged -= OnPopulationChanged;
        GameEvents.HappinessChanged -= OnHappinessChanged;
        GameEvents.CityLoaded -= SyncCityState;
        GameEvents.PowerChanged -= OnPowerChanged;
        GameEvents.WaterChanged -= OnWaterChanged;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.AgeChanged -= OnAgeChanged;
        GameEvents.ResearchChanged -= OnResearchChanged;
        GameEvents.Redeveloped -= OnRedeveloped;
    }

    private void Start()
    {
        SetToastAlpha(0f);
        float money = m_GameManager != null && m_GameManager.Economy != null ? m_GameManager.Economy.Money : 0f;
        OnMoneyChanged(money);
        SyncCityState();
    }

    private void Update()
    {
        if (m_ToastTimer <= 0f) return;

        m_ToastTimer -= Time.unscaledDeltaTime;
        SetToastAlpha(Mathf.Clamp01(m_ToastTimer / m_FadeDuration));
    }

    // Longer messages stay up longer (about 30 characters a second).
    private void ShowToast(string message)
    {
        message = KeyBindings.Fill(message);
        if (m_ToastText != null) m_ToastText.text = message;
        m_ToastTimer = Mathf.Max(m_ToastDuration, message.Length / 30f);
        SetToastAlpha(1f);
    }

    private void OnInsufficientFunds(float cost)
    {
        float money = m_GameManager != null ? m_GameManager.Economy.Money : 0f;
        ShowToast($"<color=#F26659>Not enough money</color> — costs ${cost:N0}, you have ${Mathf.Max(0f, money):N0}");
    }

    // After a load or new game, treat the current state as already announced.
    private void SyncCityState()
    {
        if (m_GameManager == null || m_GameManager.Population == null) return;

        PopulationSystem population = m_GameManager.Population;
        m_MilestoneIndex = 0;
        while (m_MilestoneIndex < m_PopulationMilestones.Length && population.Population >= m_PopulationMilestones[m_MilestoneIndex])
        {
            m_MilestoneIndex++;
        }
        m_WasUnhappy = population.AverageHappiness < m_GameManager.Balance.LowHappinessThreshold;

        PowerSystem power = m_GameManager.Simulation.Power;
        m_HadPower = power.Supply > 0;
        m_WasShort = power.Supply > 0 && power.UnpoweredCells > 0;
        m_NudgedNoPower = false;   // a loaded city without power still gets the hint

        WaterStatus water = m_GameManager.Simulation.Water.Status;
        m_HadWater = water.Supply > 0;
        m_WasDry = water.Supply > 0 && water.DryCells > 0;
        m_NudgedNoWater = false;

        TechSystem tech = m_GameManager.Simulation.Tech;
        m_AnnouncedReady = tech != null && tech.GetAdvanceStatus(population.Population).Ready;
        m_AnnouncedRebuild = false;
        m_AnnouncedLandValue = AnyHeldByLandValue();   // a new city starts over
        m_AnnouncedTraffic = population.Happiness.Traffic <= -0.01f;
        HappinessBreakdown happiness = population.Happiness;
        foreach (ServiceKind kind in new[] { ServiceKind.Order, ServiceKind.Fire, ServiceKind.Health })
        {
            m_AnnouncedCivic[(int)kind] = -CivicTerm(happiness, kind) >= CivicToastThreshold;
        }
    }

    // --- Civic services (M14) ---

    private static float CivicTerm(HappinessBreakdown happiness, ServiceKind kind)
    {
        switch (kind)
        {
            case ServiceKind.Order: return happiness.Crime;
            case ServiceKind.Fire: return happiness.Fire;
            default: return happiness.Health;
        }
    }

    // Once per city and line: the first time crime, fire risk or sickness costs 2% happiness while the
    // player can build something against it.
    private void CheckCivicNeeds()
    {
        HappinessBreakdown happiness = m_GameManager.Population.Happiness;
        foreach (ServiceKind kind in new[] { ServiceKind.Order, ServiceKind.Fire, ServiceKind.Health })
        {
            if (m_AnnouncedCivic[(int)kind] || -CivicTerm(happiness, kind) < CivicToastThreshold) continue;
            BuildingDefinition best = m_GameManager.BestCivic(kind);
            if (best == null) continue;
            m_AnnouncedCivic[(int)kind] = true;
            string need = kind == ServiceKind.Order ? "<color=#F2665A>Crime</color> is rising"
                : kind == ServiceKind.Fire ? "<color=#F2733F>Fire risk</color> worries residents"
                : "<color=#59D966>Sickness</color> is spreading";
            ShowToast($"{need} (−{-CivicTerm(happiness, kind):P0} happiness) — build a <b>{best.DisplayName}</b> near crowded homes. Services view shows where it's needed.");
            return;
        }
    }

    // Placed civic buildings that a just-unlocked building outdates, by name.
    private string OutdatedBy(BuildingDefinition unlocked)
    {
        if (unlocked.CivicKind == ServiceKind.None) return null;
        var names = new List<string>();
        foreach (BuildingInstance placed in m_GameManager.SourceBuildings)
        {
            BuildingDefinition def = placed != null ? placed.Definition : null;
            if (def == null || def == unlocked || def.CivicKind != unlocked.CivicKind) continue;
            if (m_GameManager.ReplacementFor(def) == unlocked && !names.Contains(def.DisplayName)) names.Add(def.DisplayName);
        }
        return names.Count > 0 ? string.Join(" and ", names) : null;
    }

    // --- Land value (M12) ---

    private bool AnyHeldByLandValue()
    {
        GridData grid = m_GameManager.Grid;
        GrowthSystem growth = m_GameManager.Simulation.Growth;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (growth.IsHeldByLandValue(new Vector2Int(x, y))) return true;
            }
        }
        return false;
    }

    // Once per city: the first time commute congestion costs happiness (M16).
    private void CheckTraffic()
    {
        if (m_AnnouncedTraffic || m_GameManager.Population.Happiness.Traffic > -0.01f) return;
        m_AnnouncedTraffic = true;
        ShowToast("<color=#F2665A>Roads are jamming</color> — commutes cost residents happiness. Upgrade the busiest streets (Traffic view shows them in red).");
    }

    // Once per city: the first home or shop that can't reach level 3 for lack of land value.
    private void CheckLandValue()
    {
        if (m_AnnouncedLandValue || !AnyHeldByLandValue()) return;
        m_AnnouncedLandValue = true;
        ShowToast($"Some homes and shops can't reach level 3: their <color=#F2C14E>land value</color> is below " +
                  $"{m_GameManager.Balance.LandValueForLevel3:P0}. Parks and kept historic blocks raise it, pollution lowers it — see the Value view.");
    }

    // --- Ages & research (M11) ---

    private void OnTechCompleted(string techId)
    {
        TechSystem tech = m_GameManager != null ? m_GameManager.Simulation.Tech : null;
        TechDefinition done = tech?.Techs.GetById(techId);
        if (done == null) return;

        var unlocked = new List<string>();
        var replaces = new List<string>();
        if (m_GameManager.Buildings != null)
        {
            foreach (BuildingDefinition def in m_GameManager.Buildings.Entries)
            {
                if (def == null || def.RequiredTech != techId) continue;
                unlocked.Add(def.DisplayName);
                string outdated = OutdatedBy(def);
                if (outdated != null) replaces.Add($"the {def.DisplayName} replaces your {outdated}");
            }
        }
        foreach (RoadTierDefinition tier in tech.Techs.RoadTiers)
        {
            if (tier != null && tier.RequiredTech == done) unlocked.Add($"{tier.DisplayName} road");
        }
        string message = $"<color=#73D973>Research complete:</color> {done.DisplayName}";
        if (unlocked.Count > 0) message += $" — <b>{string.Join(", ", unlocked)}</b> unlocked";
        if (replaces.Count > 0) message += $" ({string.Join("; ", replaces)} — they keep working, but are outdated)";
        var ordinances = new List<string>();
        foreach (OrdinanceDefinition ordinance in tech.Techs.Ordinances)
        {
            if (ordinance != null && ordinance.RequiredTech == done) ordinances.Add(ordinance.DisplayName);
        }
        if (ordinances.Count > 0) message += $" — ordinance <b>{string.Join(", ", ordinances)}</b> available in Budget";
        if (tech.Active == null) message += ". Pick the next project in Research.";
        ShowToast(message);
    }

    private void OnAgeChanged(int age)
    {
        TechSystem tech = m_GameManager.Simulation.Tech;
        AgeDefinition def = tech.Ages[age];
        string message = $"<color=#F2CC4D>Welcome to the {def.DisplayName}!</color> New buildings grow in its style; older blocks are rebuilt unless you keep them historic.";
        if (def.UpgradesNeedPower && !m_GameManager.PowerUnlocked)
        {
            message += " Buildings now need <color=#FFD133>power</color> to grow past level 1 — research Electricity and build a Power Plant.";
        }
        if (def.Water == WaterRule.Piped && age > 0 && tech.Ages[age - 1].Water != WaterRule.Piped)
        {
            message += PipedWaterUnlocked()
                ? " Water now comes from <color=#59A6F2>water towers</color> on the roads — wells no longer count. Build a Water Tower beside a road."
                : " Water now comes from <color=#59A6F2>water towers</color> on the roads — wells no longer count. Research Waterworks and build a Water Tower.";
        }
        ShowToast(message);
        m_AnnouncedReady = false;
        m_AnnouncedRebuild = false;
    }

    // Once per age, when the checklist is first met and the advance isn't planned yet.
    private void OnResearchChanged()
    {
        if (m_AnnouncedReady || m_GameManager == null) return;
        TechSystem tech = m_GameManager.Simulation.Tech;
        if (tech == null) return;

        AdvanceStatus status = tech.GetAdvanceStatus(m_GameManager.Population.Population);
        if (!status.Ready || tech.IsPlanned(tech.NextAdvance())) return;
        m_AnnouncedReady = true;
        ShowToast($"<color=#F2CC4D>Ready to advance to the {tech.Ages[status.NextAge].DisplayName}</color> — start it in Research ({status.RpCost:N0} RP).");
    }

    private void OnRedeveloped(int count)
    {
        if (m_AnnouncedRebuild || m_GameManager == null || m_GameManager.Simulation.Tech == null) return;
        m_AnnouncedRebuild = true;
        string age = m_GameManager.Simulation.Tech.CurrentAgeDefinition.DisplayName;
        ShowToast($"Builders are rebuilding older blocks in the {age} style. Select a building to keep it historic.");
    }

    // Each fires once per change of state, not on every grid change.
    private void OnPowerChanged(int supply, int demand, int unpoweredCells)
    {
        if (m_GameManager == null || m_GameManager.Population == null) return;
        if (!m_GameManager.PowerUnlocked) return;   // no power toasts before Electricity

        if (supply > 0 && !m_HadPower)
        {
            ShowToast("<color=#FFD133>Power plant online</color> — buildings along its roads can now grow past level 1. [V] shows the power grid.");
        }
        else if (supply == 0 && m_HadPower && demand > 0)
        {
            ShowToast("<color=#F26659>Power lost</color> — no plant is connected to a road. Buildings can't upgrade and residents are unhappy.");
        }
        m_HadPower = supply > 0;

        bool shortage = supply > 0 && unpoweredCells > 0;
        if (shortage && !m_WasShort)
        {
            ShowToast($"<color=#F26659>Power shortage</color> — {unpoweredCells} building{(unpoweredCells == 1 ? "" : "s")} without power. Build another plant or connect their roads. [V] shows the power grid.");
        }
        m_WasShort = shortage;

        int grace = m_GameManager.Balance.SmallTownGracePopulation;
        if (supply == 0 && !m_NudgedNoPower && m_GameManager.Population.Population >= grace)
        {
            ShowToast("Buildings can't grow past level 1 without power — build a <color=#FFD133>Power Plant</color> beside a road.");
            m_NudgedNoPower = true;
        }
    }

    private bool PipedWaterUnlocked()
    {
        if (m_GameManager.Buildings == null) return true;
        foreach (BuildingDefinition def in m_GameManager.Buildings.Entries)
        {
            if (def != null && def.WaterSupply > 0 && m_GameManager.CanBuild(def)) return true;
        }
        return false;
    }

    // M13, like power: each fires once per change of state. Well ages only get the one-time nudge.
    private void OnWaterChanged(WaterStatus status)
    {
        if (m_GameManager == null || m_GameManager.Population == null) return;
        if (!m_GameManager.WaterUnlocked) return;
        int population = m_GameManager.Population.Population;

        if (status.Mode == WaterRule.Coverage)
        {
            if (status.DryCells > 0 && !m_NudgedNoWater && population >= m_WellNudgePopulation)
            {
                ShowToast("Buildings need water to grow past level 1 — dig a <color=#59A6F2>Well</color> nearby (it waters the blocks around it). [V] shows where the water reaches.");
                m_NudgedNoWater = true;
            }
            m_HadWater = false;
            m_WasDry = false;
            return;
        }
        if (status.Mode != WaterRule.Piped) return;

        if (status.Supply > 0 && !m_HadWater)
        {
            ShowToast("<color=#59A6F2>Water tower online</color> — buildings along its roads can now grow past level 1. [V] shows the water network.");
        }
        else if (status.Supply == 0 && m_HadWater && status.Demand > 0)
        {
            ShowToast("<color=#F26659>Water lost</color> — no tower is connected to a road. Buildings can't upgrade and residents are unhappy.");
        }
        m_HadWater = status.Supply > 0;

        bool shortage = status.Supply > 0 && status.DryCells > 0;
        if (shortage && !m_WasDry)
        {
            ShowToast($"<color=#F26659>Water shortage</color> — {status.DryCells} building{(status.DryCells == 1 ? "" : "s")} without water. Build another tower or connect their roads. [V] shows the water network.");
        }
        m_WasDry = shortage;

        if (status.Supply == 0 && status.Demand > 0 && !m_NudgedNoWater && population >= m_GameManager.Balance.SmallTownGracePopulation)
        {
            ShowToast("Buildings can't grow past level 1 without water — build a <color=#59A6F2>Water Tower</color> beside a road.");
            m_NudgedNoWater = true;
        }
    }

    private void OnPopulationChanged(int population, int jobs)
    {
        int reached = -1;
        while (m_MilestoneIndex < m_PopulationMilestones.Length && population >= m_PopulationMilestones[m_MilestoneIndex])
        {
            reached = m_PopulationMilestones[m_MilestoneIndex++];
        }
        if (reached > 0) ShowToast($"Population milestone: {reached:N0} residents!");
        else
        {
            CheckLandValue();
            CheckCivicNeeds();
            CheckTraffic();
        }
    }

    // Fires once per drop below the threshold, not every unhappy day.
    private void OnHappinessChanged(float happiness)
    {
        if (m_GameManager == null || m_GameManager.Balance == null) return;

        bool unhappy = happiness < m_GameManager.Balance.LowHappinessThreshold;
        if (unhappy && !m_WasUnhappy)
        {
            ShowToast($"Happiness below {m_GameManager.Balance.LowHappinessThreshold:P0} — residents are leaving. Hover Happiness to see why.");
        }
        m_WasUnhappy = unhappy;
    }

    private void OnMoneyChanged(float money)
    {
        if (m_Banner == null) return;

        bool inDebt = money < 0f;
        if (m_Banner.activeSelf != inDebt) m_Banner.SetActive(inDebt);
        if (inDebt && m_BannerText != null)
        {
            string loanHint = m_GameManager != null && m_GameManager.Simulation.Budget.CanBorrow ? " A loan in Budget can tide you over." : "";
            m_BannerText.text = $"In debt: -${-money:N0}. Raise taxes or cut upkeep — roads and buildings are locked until you're out of debt.{loanHint}";
        }
    }

    private void SetToastAlpha(float alpha)
    {
        if (m_Toast == null) return;
        m_Toast.alpha = alpha;
        m_Toast.gameObject.SetActive(alpha > 0f);
    }
}
