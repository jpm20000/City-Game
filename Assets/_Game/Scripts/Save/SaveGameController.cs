using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Single-slot save/load (F5 / F9, plus the HUD game menu) and New City. Rebuilds the scene from a
// SaveData: clear placed buildings -> restore grid -> re-place buildings -> restore sim -> calendar.
public sealed class SaveGameController : MonoBehaviour
{
    private const string k_FileName = "city.json";

    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private InputReader m_InputReader;

    public string SavePath => Path.Combine(Application.persistentDataPath, k_FileName);
    public bool HasSave => File.Exists(SavePath);

    private void Update()
    {
        if (m_InputReader == null) return;

        if (m_InputReader.QuickSavePressed) Save();
        else if (m_InputReader.QuickLoadPressed) Load();
    }

    public bool Save()
    {
        if (!IsReady()) return false;

        SaveData data = SaveSystem.Capture(m_GameManager.Grid, m_GameManager.Simulation);

        TimeManager clock = m_GameManager.Clock;
        if (clock != null)
        {
            data.Day = clock.Day;
            data.Month = clock.Month;
            data.Year = clock.Year;
            data.Speed = (int)clock.Speed;
        }

        // Ordered by placement so the file is stable between saves of the same city.
        List<BuildingInstance> buildings = new(m_Placement.PlacedBuildings);
        buildings.Sort((a, b) => a.OccupantId.CompareTo(b.OccupantId));
        foreach (BuildingInstance building in buildings)
        {
            data.Buildings.Add(new BuildingRecord(building.Definition.Id, building.Origin.x, building.Origin.y, building.Rotation));
        }

        if (!SaveSystem.TryWrite(SavePath, data, out string error))
        {
            Debug.LogError($"SaveGameController: save to {SavePath} failed: {error}", this);
            GameEvents.RaiseNotification($"Save failed: {error}");
            return false;
        }

        GameEvents.RaiseNotification($"City saved — Day {data.Day}, Month {data.Month}, Year {data.Year}");
        return true;
    }

    public bool Load()
    {
        if (!IsReady()) return false;

        if (!SaveSystem.TryRead(SavePath, out SaveData data, out string error, m_GameManager.Ages, m_GameManager.Techs))
        {
            GameEvents.RaiseNotification($"Couldn't load: {error}");
            return false;
        }

        int skipped = Apply(data);
        GameEvents.RaiseNotification(skipped == 0
            ? $"City loaded — Day {data.Day}, Month {data.Month}, Year {data.Year}"
            : $"City loaded — {skipped} building(s) could not be restored");
        return true;
    }

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
    public void NewCity(Vector2Int size, int startAge)
    {
        if (!IsReady()) return;

        Apply(SaveSystem.CreateNew(size.x, size.y, m_GameManager.Balance, m_GameManager.Ages, m_GameManager.Techs, startAge));
        string age = m_GameManager.CurrentAgeName;
        GameEvents.RaiseNotification(age != null ? $"New city — {size.x}×{size.y}, {age}" : $"New city — {size.x}×{size.y}");
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

        m_GameManager.RaiseStateEvents();
        GameEvents.RaiseCityLoaded();
        return skipped;
    }

    private bool IsReady()
    {
        if (m_GameManager != null && m_GameManager.Simulation != null && m_Placement != null) return true;

        Debug.LogError("SaveGameController: GameManager or PlacementController not assigned.", this);
        return false;
    }
}
