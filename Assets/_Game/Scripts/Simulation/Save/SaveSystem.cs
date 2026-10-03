using System;
using System.IO;
using UnityEngine;

// Pure save/load helpers: capture/apply the grid + simulation state and JSON file IO. The runtime
// layer (SaveGameController) adds the calendar and placed buildings and rebuilds the scene. Older
// save versions are migrated on read (SaveMigrations), so pass the game's age/tech databases.
public static class SaveSystem
{
    // Largest map side a save may declare (guards against huge allocations from a bad file).
    public const int MaxMapSize = 256;

    // Grid, economy and population. Calendar and buildings are filled in by the caller.
    public static SaveData Capture(GridData grid, SimulationSystem sim)
    {
        if (grid == null) throw new ArgumentNullException(nameof(grid));
        if (sim == null) throw new ArgumentNullException(nameof(sim));

        EconomySystem economy = sim.Economy;
        SaveData data = new SaveData
        {
            Money = economy.Money,
            IncomePerDay = economy.IncomePerDay,
            ExpensePerDay = economy.ExpensePerDay,
            TaxResidential = economy.TaxResidential,
            TaxCommercial = economy.TaxCommercial,
            TaxIndustrial = economy.TaxIndustrial,
            Population = sim.Population.Population,
            Happiness = sim.Population.AverageHappiness,
            Width = grid.Width,
            Height = grid.Height,
            Zones = grid.ExportZones(),
            Roads = grid.ExportRoads(),
            Levels = grid.ExportLevels(),
            BuiltAges = grid.ExportBuiltAges(),
            Historic = grid.ExportHistoric(),
            Pipes = grid.ExportPipes(),
        };
        TechSystem tech = sim.Tech;
        if (tech != null)
        {
            data.Age = tech.CurrentAge;
            foreach (TechDefinition researched in tech.Researched) data.Researched.Add(researched.Id);
            data.ActiveResearch = tech.Active != null ? tech.Active.Id : "";
            data.ResearchProgress = tech.Progress;
            foreach (ResearchProject project in tech.Queue) data.ResearchQueue.Add(project.Id);
        }
        return data;
    }

    // An empty map with the configured starting values. With ages, the city starts in startAge
    // (-1 = the Industrial age): that age's year, money and starting research.
    public static SaveData CreateNew(int width, int height, BalanceConfig config,
        AgeDatabase ages = null, TechDatabase techs = null, int startAge = -1)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        int count = width * height;
        SaveData data = new SaveData
        {
            Money = config.StartingMoney,
            TaxResidential = config.TaxResidential,
            TaxCommercial = config.TaxCommercial,
            TaxIndustrial = config.TaxIndustrial,
            Happiness = config.StartingHappiness,
            Width = width,
            Height = height,
            Zones = new byte[count],
            Roads = new byte[count],
            Levels = new byte[count],
            BuiltAges = new byte[count],
            Historic = new byte[count],
            Pipes = new byte[count],
        };
        if (ages != null && techs != null)
        {
            int age = startAge >= 0 ? startAge : Math.Max(0, ages.Legacy);
            if (!ages.IsValidIndex(age)) throw new ArgumentOutOfRangeException(nameof(startAge));
            data.Age = age;
            data.Year = ages[age].StartYear;
            data.Money = ages[age].StartingMoney;
            foreach (TechDefinition tech in TechSystem.StartingTechs(ages, techs, age)) data.Researched.Add(tech.Id);
        }
        return data;
    }

    // Step 1 of a load: replace grid contents, resizing the map to the save's size if it differs
    // (placed buildings must already be released).
    public static void ApplyGrid(SaveData data, GridData grid)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (grid == null) throw new ArgumentNullException(nameof(grid));
        if (data.Width != grid.Width || data.Height != grid.Height) grid.Resize(data.Width, data.Height);
        grid.Import(data.Zones, data.Roads, data.Levels, data.BuiltAges, data.Historic, data.Pipes);
    }

    // Step 2, after the grid is restored and SimulationSystem.Modifiers/Sources reflect placed buildings.
    // Research is restored first (demand and happiness read the researched techs). Returns how many
    // saved tech / project Ids were unknown or no longer valid and were dropped (warn, don't fail).
    public static int ApplySimulation(SaveData data, SimulationSystem sim)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (sim == null) throw new ArgumentNullException(nameof(sim));

        int dropped = 0;
        TechSystem tech = sim.Tech;
        if (tech != null)
        {
            if (tech.Ages.IsValidIndex(data.Age))
            {
                dropped = tech.Restore(data.Age, data.Researched, data.ActiveResearch, data.ResearchProgress, data.ResearchQueue);
            }
            else
            {
                tech.StartNew(Math.Max(0, tech.Ages.Legacy));   // not migrated with these databases
            }
        }

        sim.Restore(
            data.Money, data.IncomePerDay, data.ExpensePerDay,
            data.TaxResidential, data.TaxCommercial, data.TaxIndustrial,
            data.Population, data.Happiness);
        return dropped;
    }

    public static string ToJson(SaveData data)
    {
        return JsonUtility.ToJson(data);
    }

    // Returns false (with a player-readable reason) for malformed, incompatible or incomplete saves.
    // Older versions are migrated to the current one for the given databases (null = no ages).
    public static bool TryFromJson(string json, out SaveData data, out string error,
        AgeDatabase ages = null, TechDatabase techs = null)
    {
        data = null;
        try
        {
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (ArgumentException e)
        {
            error = $"Save file is corrupt ({e.Message}).";
            return false;
        }

        if (data == null)
        {
            error = "Save file is empty.";
            return false;
        }
        if (data.Width <= 0 || data.Height <= 0 || data.Width > MaxMapSize || data.Height > MaxMapSize)
        {
            error = $"Save file has an invalid map size ({data.Width}x{data.Height}).";
            data = null;
            return false;
        }

        if (!SaveMigrations.TryMigrate(data, ages, techs, out error))
        {
            data = null;
            return false;
        }

        int count = data.Width * data.Height;
        if (data.Zones?.Length != count || data.Roads?.Length != count || data.Levels?.Length != count
            || data.BuiltAges?.Length != count || data.Historic?.Length != count || data.Pipes?.Length != count)
        {
            error = "Save file has missing or mismatched map data.";
            data = null;
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryWrite(string path, SaveData data, out string error)
    {
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write to a temp file first so a failed write never truncates the existing save.
            string temp = path + ".tmp";
            File.WriteAllText(temp, ToJson(data));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            error = e.Message;
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryRead(string path, out SaveData data, out string error,
        AgeDatabase ages = null, TechDatabase techs = null)
    {
        data = null;
        if (!File.Exists(path))
        {
            error = "No save file found.";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            error = e.Message;
            return false;
        }
        return TryFromJson(json, out data, out error, ages, techs);
    }
}
