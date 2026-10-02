using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingDatabase", menuName = "CityBuilder/Building Database")]
public sealed class BuildingDatabase : ScriptableObject
{
    [SerializeField] private List<BuildingDefinition> m_Entries = new();

    public IReadOnlyList<BuildingDefinition> Entries => m_Entries;

    public BuildingDefinition GetById(string id)
    {
        foreach (BuildingDefinition def in m_Entries)
        {
            if (def != null && def.Id == id)
            {
                return def;
            }
        }
        return null;
    }
}
