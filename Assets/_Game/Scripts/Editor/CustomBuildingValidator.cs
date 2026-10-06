using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// M25: the checks a custom placeable building must pass before it goes into the BuildingDatabase (the Add Custom
// Building wizard runs it, and a test runs it on every shipped definition so the rules and the content agree).
// The prefab is checked by ArtContract.ValidatePlaceable; this adds the definition's own fields.
public static class CustomBuildingValidator
{
    // others = the database's other entries (for the unique Id), techIds = every TechDefinition.Id (null = skip the tech check).
    public static bool Validate(BuildingDefinition def, IEnumerable<BuildingDefinition> others, ICollection<string> techIds, List<string> errors)
    {
        int before = errors.Count;
        if (def == null)
        {
            errors.Add("no definition");
            return false;
        }

        string id = def.Id;
        string label = string.IsNullOrEmpty(id) ? def.name : id;
        if (string.IsNullOrWhiteSpace(id)) errors.Add($"{label}: empty Id");
        else if (id != id.Trim() || id.IndexOfAny(new[] { ' ', '/', '\\', ',', ';' }) >= 0) errors.Add($"{label}: Id must not contain spaces or separators");
        if (string.IsNullOrWhiteSpace(def.DisplayName)) errors.Add($"{label}: empty display name");

        if (others != null && !string.IsNullOrEmpty(id))
        {
            foreach (BuildingDefinition other in others)
            {
                if (other != null && other != def && other.Id == id) errors.Add($"{label}: Id is already used by '{other.name}'");
            }
        }

        if (def.Category == BuildingCategory.Zone || def.Category == BuildingCategory.Road)
        {
            errors.Add($"{label}: category {def.Category} is not placeable here (use Service, Utility or Decoration)");
        }
        if (def.Cost < 0) errors.Add($"{label}: negative cost");
        if (def.UpkeepPerDay < 0f) errors.Add($"{label}: negative upkeep");
        if (def.HousingCapacity < 0 || def.JobsProvided < 0) errors.Add($"{label}: negative housing or jobs");
        if (def.Size.x < 1 || def.Size.y < 1) errors.Add($"{label}: size {def.Size.x}x{def.Size.y} must be at least 1x1");

        if (!string.IsNullOrEmpty(def.RequiredTech) && techIds != null && !techIds.Contains(def.RequiredTech))
        {
            errors.Add($"{label}: RequiredTech '{def.RequiredTech}' is not a tech in the TechDatabase");
        }

        if (def.Prefab == null) errors.Add($"{label}: no prefab");
        else ArtContract.ValidatePlaceable(def.Prefab, errors);

        return errors.Count == before;
    }
}
