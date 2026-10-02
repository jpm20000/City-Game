using System;
using System.IO;
using UnityEngine;

// Pure save/load helpers: capture/apply the grid + simulation state and JSON file IO. The runtime
// layer (SaveGameController) adds the calendar and placed buildings and rebuilds the scene.
public static class SaveSystem
{
    // Grid, economy and population. Calendar and buildings are filled in by the caller.
    public static SaveData Capture(GridData grid, SimulationSystem sim)
    {
        if (grid == null) throw new ArgumentNullException(nameof(grid));
        if (sim == null) throw new ArgumentNullException(nameof(sim));

        EconomySystem economy = sim.Economy;
        return new SaveData
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
        };
    }

    // An empty map with the configured starting values.
    public static SaveData CreateNew(int width, int height, BalanceConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        int count = width * height;
        return new SaveData
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
        };
    }

    // Step 1 of a load: replace grid contents (placed buildings must already be released).
    public static void ApplyGrid(SaveData data, GridData grid)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (grid == null) throw new ArgumentNullException(nameof(grid));
        if (data.Width != grid.Width || data.Height != grid.Height)
        {
            throw new ArgumentException($"Save is {data.Width}x{data.Height}, map is {grid.Width}x{grid.Height}.");
        }
        grid.Import(data.Zones, data.Roads, data.Levels);
    }

    // Step 2, after the grid is restored and SimulationSystem.Modifiers/Sources reflect placed buildings.
    public static void ApplySimulation(SaveData data, SimulationSystem sim)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (sim == null) throw new ArgumentNullException(nameof(sim));

        sim.Restore(
            data.Money, data.IncomePerDay, data.ExpensePerDay,
            data.TaxResidential, data.TaxCommercial, data.TaxIndustrial,
            data.Population, data.Happiness);
    }

    public static string ToJson(SaveData data)
    {
        return JsonUtility.ToJson(data);
    }

    // Returns false (with a player-readable reason) for malformed, incompatible or incomplete saves.
    public static bool TryFromJson(string json, out SaveData data, out string error)
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
        if (data.Version != SaveData.CurrentVersion)
        {
            error = $"Save file version {data.Version} is not supported (expected {SaveData.CurrentVersion}).";
            data = null;
            return false;
        }

        int count = data.Width * data.Height;
        if (count <= 0 || data.Zones?.Length != count || data.Roads?.Length != count || data.Levels?.Length != count)
        {
            error = "Save file has missing or mismatched map data.";
            data = null;
            return false;
        }

        data.Buildings ??= new();
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

    public static bool TryRead(string path, out SaveData data, out string error)
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
        return TryFromJson(json, out data, out error);
    }
}
