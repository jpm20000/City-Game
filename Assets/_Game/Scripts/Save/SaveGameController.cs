using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// Named saves, quicksave / autosave and New City (M19a; single slot before). Rebuilds the scene from a SaveData: clear
// placed buildings -> restore grid -> re-place buildings -> restore sim -> calendar. The files live in SaveSlots.Root
// (persistentDataPath/Saves, or `-savesDir <path>` on the command line; Play-mode checks can set SaveSlots.Root after
// startup to keep the player's saves out of it).
public sealed class SaveGameController : MonoBehaviour
{
    private const string k_LegacyFileName = "city.json";
    private const string k_SavesFolder = "Saves";
    private const string k_SavesDirArg = "-savesDir";
    private const string k_ShowcaseFileName = "showcase.json";

    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private InputReader m_InputReader;

    // The Manual save the current city was last saved to or loaded from ("" = none yet): what Save() overwrites.
    public string CurrentName { get; private set; } = "";
    public string CityName { get; private set; } = "";
    // Tutorial progress carried through saves (-1 = no tutorial); the tutorial itself arrives in M19f.
    public int Tutorial { get; set; } = SaveData.NoTutorial;
    // True when the city changed since it was last saved or loaded.
    // The main menu's showcase city is never dirty: leaving it asks nothing.
    public bool Dirty { get => m_Dirty && !ShowcaseActive; private set => m_Dirty = value; }
    private bool m_Dirty;
    public bool ShowcaseActive { get; private set; }

    public string SavesFolder => SaveSlots.HasRoot ? SaveSlots.Root : "";
    public bool HasSave => SaveSlots.HasAny();

    private void Awake()
    {
        SaveSlots.Root = ResolveRoot();
        ImportFromOldIdentity();
        string legacy = Path.Combine(Application.persistentDataPath, k_LegacyFileName);
        if (SaveSlots.ImportLegacy(legacy, m_GameManager != null ? m_GameManager.Ages : null, m_GameManager != null ? m_GameManager.Techs : null))
        {
            Debug.Log($"SaveGameController: imported {legacy} as '{SaveSlots.LegacyImportName}'.", this);
        }
    }

    // M19g: the game used to be "DefaultCompany / City Game"; its saves (and the pre-M19 city.json) come over once.
    // Not with a -savesDir override (tests and smoke runs stay away from real folders).
    private void ImportFromOldIdentity()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), k_SavesDirArg) >= 0) return;
        string localLow = Path.GetDirectoryName(Path.GetDirectoryName(Application.persistentDataPath));
        if (string.IsNullOrEmpty(localLow)) return;
        string oldData = Path.Combine(localLow, "DefaultCompany", "City Game");
        if (Path.GetFullPath(oldData) == Path.GetFullPath(Application.persistentDataPath)) return;
        int count = SaveSlots.ImportFolder(Path.Combine(oldData, k_SavesFolder));
        if (count > 0) Debug.Log($"SaveGameController: copied {count} save(s) from {oldData}.", this);
        SaveSlots.ImportLegacy(Path.Combine(oldData, k_LegacyFileName), m_GameManager != null ? m_GameManager.Ages : null,
            m_GameManager != null ? m_GameManager.Techs : null);
    }

    private static string ResolveRoot()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == k_SavesDirArg && !string.IsNullOrWhiteSpace(args[i + 1])) return args[i + 1];
        }
        return Path.Combine(Application.persistentDataPath, k_SavesFolder);
    }

    private void OnEnable()
    {
        GameEvents.CellChanged += OnCityChanged;
        GameEvents.DateChanged += OnDateChanged;
        GameEvents.BudgetChanged += MarkDirty;
    }

    private void OnDisable()
    {
        GameEvents.CellChanged -= OnCityChanged;
        GameEvents.DateChanged -= OnDateChanged;
        GameEvents.BudgetChanged -= MarkDirty;
    }

    private void OnCityChanged(Vector2Int cell) => Dirty = true;
    private void OnDateChanged(int day, int month, int year) => Dirty = true;
    private void MarkDirty() => Dirty = true;

    private void Update()
    {
        if (m_InputReader == null) return;

        if (m_InputReader.QuickSavePressed) QuickSave();
        else if (m_InputReader.QuickLoadPressed) Load();
    }

    // --- Saving ---

    // The HUD Save button: the city's own save (the Manual slot it was last saved to or loaded from), or a new one
    // named after the city (or "City <date>").
    public bool Save()
    {
        string name = CurrentName;
        if (string.IsNullOrEmpty(name) || SaveSlots.KindOf(name) != SaveKind.Manual)
        {
            string city = string.IsNullOrWhiteSpace(CityName) ? $"City {DateTime.Now:yyyy-MM-dd}" : CityName;
            name = SaveSlots.UniqueName(city);
        }
        return SaveAs(name, SaveKind.Manual);
    }

    // F5: one quicksave per city, overwritten each time.
    public bool QuickSave() => SaveAs(SaveSlots.QuickName(CityName), SaveKind.Quick);

    // The timed autosave: the oldest of the rotating autosave slots.
    public bool Autosave() => SaveAs(SaveSlots.NextAutosave(), SaveKind.Auto);

    // Writes the city to the named slot (sanitized; an existing save of that name is replaced). Manual saves become
    // the city's current save; quick and autosaves leave that alone, so Save() keeps its own file.
    public bool SaveAs(string name, SaveKind kind = SaveKind.Manual)
    {
        if (!IsReady()) return false;

        name = SaveSlots.SanitizeName(name);
        SaveData data = Capture();
        string ageName = m_GameManager.CurrentAgeName;
        SaveSummary summary = SaveSummary.From(data, kind, DateTime.UtcNow.Ticks, Application.version, ageName);
        byte[] png = ThumbnailCapture.Capture(Camera.main);

        if (!SaveSlots.Write(name, data, summary, png, out string error))
        {
            Debug.LogError($"SaveGameController: save '{name}' failed: {error}", this);
            GameEvents.RaiseNotification($"Save failed: {error}");
            return false;
        }

        if (kind == SaveKind.Manual) CurrentName = name;
        Dirty = false;
        if (kind != SaveKind.Auto)
        {
            GameEvents.RaiseNotification($"City saved — {name} (Day {data.Day}, Month {data.Month}, Year {data.Year})");
            AudioController.Play(SfxId.Save);
        }
        return true;
    }

    private SaveData Capture()
    {
        SaveData data = SaveSystem.Capture(m_GameManager.Grid, m_GameManager.Simulation);
        data.CityName = CityName ?? "";
        data.Tutorial = Tutorial;

        TimeManager clock = m_GameManager.Clock;
        if (clock != null)
        {
            data.Day = clock.Day;
            data.Month = clock.Month;
            data.Year = clock.Year;
            data.Speed = (int)GameFlow.SpeedToSave(clock);
        }

        // Ordered by placement so the file is stable between saves of the same city.
        List<BuildingInstance> buildings = new(m_Placement.PlacedBuildings);
        buildings.Sort((a, b) => a.OccupantId.CompareTo(b.OccupantId));
        foreach (BuildingInstance building in buildings)
        {
            data.Buildings.Add(new BuildingRecord(building.Definition.Id, building.Origin.x, building.Origin.y, building.Rotation));
        }
        return data;
    }

    // Rename / delete from the save browser, keeping CurrentName pointing at the city's own file.
    public bool Rename(string oldName, string newName, out string finalName, out string error)
    {
        if (!SaveSlots.Rename(oldName, newName, out finalName, out error)) return false;
        if (CurrentName == oldName) CurrentName = finalName;
        return true;
    }

    public bool Delete(string name)
    {
        if (!SaveSlots.Delete(name)) return false;
        if (CurrentName == name) CurrentName = "";
        return true;
    }

    // --- Loading ---

    // F9 / the HUD Load button: the newest save of the current city, else the newest save of all.
    public bool Load()
    {
        if (!IsReady()) return false;

        List<SaveSummary> saves = SaveSlots.List(m_GameManager.Ages, m_GameManager.Techs);
        SaveSummary pick = null;
        foreach (SaveSummary save in saves)
        {
            if (save.Damaged) continue;
            if (!string.IsNullOrEmpty(CityName) && save.CityName == CityName) { pick = save; break; }
            if (pick == null) pick = save;
        }
        if (pick == null)
        {
            GameEvents.RaiseNotification("No saved city to load.");
            return false;
        }
        return Load(pick.Name);
    }

    public bool Load(string name)
    {
        if (!IsReady()) return false;

        if (!SaveSlots.TryRead(name, out SaveData data, out string error, m_GameManager.Ages, m_GameManager.Techs))
        {
            GameEvents.RaiseNotification($"Couldn't load: {error}");
            return false;
        }

        ShowcaseActive = false;
        int skipped = Apply(data);
        CurrentName = SaveSlots.KindOf(name) == SaveKind.Manual ? name : "";
        Dirty = false;
        GameEvents.RaiseNotification(skipped == 0
            ? $"City loaded — {name} (Day {data.Day}, Month {data.Month}, Year {data.Year})"
            : $"City loaded — {skipped} building(s) could not be restored");
        return true;
    }

    // The newest save overall (the main menu's Continue).
    public bool LoadNewest()
    {
        if (!IsReady()) return false;
        foreach (SaveSummary save in SaveSlots.List(m_GameManager.Ages, m_GameManager.Techs))
        {
            if (!save.Damaged) return Load(save.Name);
        }
        GameEvents.RaiseNotification("No saved city to load.");
        return false;
    }

    // The main menu's backdrop (M19c): StreamingAssets/showcase.json, loaded like a save but never saved or counted as
    // changed. False (and the current city stays) when the file is missing or can't be read.
    public bool LoadShowcase()
    {
        if (!IsReady()) return false;

#if UNITY_WEBGL && !UNITY_EDITOR
        // StreamingAssets is a URL here: fetch it, then apply it if the player is still on the title.
        StartCoroutine(LoadShowcaseWeb());
        return true;
#else
        string path = Path.Combine(Application.streamingAssetsPath, k_ShowcaseFileName);
        if (!SaveSystem.TryRead(path, out SaveData data, out string error, m_GameManager.Ages, m_GameManager.Techs))
        {
            Debug.LogWarning($"SaveGameController: no showcase city ({error}).", this);
            return false;
        }

        ApplyShowcase(data);
        return true;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private IEnumerator LoadShowcaseWeb()
    {
        using UnityWebRequest request = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + k_ShowcaseFileName);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"SaveGameController: no showcase city ({request.error}).", this);
            yield break;
        }
        if (GameFlow.Instance == null || !GameFlow.Instance.InMainMenu) yield break;   // the player already started a city
        if (!SaveSystem.TryFromJson(request.downloadHandler.text, out SaveData data, out string error, m_GameManager.Ages, m_GameManager.Techs))
        {
            Debug.LogWarning($"SaveGameController: no showcase city ({error}).", this);
            yield break;
        }
        GameFlow.Instance.ShowcaseApplying(true);
        try { ApplyShowcase(data); }
        finally { GameFlow.Instance.ShowcaseApplying(false); }
        m_GameManager.Clock.SetSpeed(GameSpeed.x1);
    }
#endif

    private void ApplyShowcase(SaveData data)
    {
        ShowcaseActive = false;
        int skipped = Apply(data);
        if (skipped > 0) Debug.LogWarning($"SaveGameController: the showcase city could not restore {skipped} building(s); regenerate it.", this);
        CurrentName = "";
        ShowcaseActive = true;
    }

    // --- New city ---

    // Keeps the current map size.
    public void NewCity()
    {
        if (!IsReady()) return;
        NewCity(m_GameManager.MapSize);
    }

    public void NewCity(Vector2Int size)
    {
        NewCity(size, -1);
    }

    // startAge = age index (ignored without age data); -1 = the Industrial age.
    // disasters = the Disasters & events switch (M17); the RNG starts from the clock.
    public void NewCity(Vector2Int size, int startAge, bool disasters = true, string cityName = "", bool tutorial = false)
    {
        if (!IsReady()) return;

        SaveData data = SaveSystem.CreateNew(size.x, size.y, m_GameManager.Balance, m_GameManager.Ages, m_GameManager.Techs, startAge,
            disasters, (ulong)Environment.TickCount);
        data.CityName = cityName ?? "";
        data.Tutorial = tutorial ? 0 : SaveData.NoTutorial;
        ShowcaseActive = false;
        Apply(data);
        CurrentName = "";
        Dirty = false;
        string age = m_GameManager.CurrentAgeName;
        GameEvents.RaiseNotification(age != null ? $"New city — {size.x}×{size.y}, {age}" : $"New city — {size.x}×{size.y}");
    }

    // The guided tutorial city (M19f): Medieval, 48 x 48, disasters off.
    public void StartTutorial() => NewCity(new Vector2Int(48, 48), 0, false, "Tutorial town", true);

    // The tutorial card moved on (or was skipped): the index goes into the next save.
    public void TutorialChanged(int index)
    {
        Tutorial = index;
        Dirty = true;
    }

    // Returns how many saved buildings couldn't be re-placed (unknown id or blocked footprint).
    private int Apply(SaveData data)
    {
        m_Placement.ClearAllBuildings();
        // Resizes the map (and everything mirroring it) when the size differs.
        SaveSystem.ApplyGrid(data, m_GameManager.Grid);

        int skipped = 0;
        foreach (BuildingRecord record in data.Buildings)
        {
            BuildingDefinition definition = m_GameManager.Buildings != null ? m_GameManager.Buildings.GetById(record.Id) : null;
            if (definition == null || !m_Placement.RestoreBuilding(definition, new Vector2Int(record.X, record.Y), record.Rotation & 3))
            {
                Debug.LogWarning($"SaveGameController: couldn't restore building '{record.Id}' at ({record.X}, {record.Y}).", this);
                skipped++;
            }
        }

        // After buildings, so GameManager's modifiers (housing, jobs, upkeep, services) are current.
        int dropped = SaveSystem.ApplySimulation(data, m_GameManager.Simulation);
        if (dropped > 0)
        {
            Debug.LogWarning($"SaveGameController: {dropped} saved tech or research project(s) are unknown to this game and were dropped.", this);
        }

        TimeManager clock = m_GameManager.Clock;
        if (clock != null)
        {
            clock.SetDate(data.Day, data.Month, data.Year);
            clock.SetSpeed((GameSpeed)Mathf.Clamp(data.Speed, (int)GameSpeed.Paused, (int)GameSpeed.x4));
        }

        CityName = data.CityName ?? "";
        Tutorial = data.Tutorial;
        m_GameManager.RaiseStateEvents();
        GameEvents.RaiseCityLoaded();
        Dirty = false;
        return skipped;
    }

    private bool IsReady()
    {
        if (m_GameManager != null && m_GameManager.Simulation != null && m_Placement != null) return true;

        Debug.LogError("SaveGameController: GameManager or PlacementController not assigned.", this);
        return false;
    }
}
