using System;
using System.Collections.Generic;
using UnityEngine;

// The ages in historical order. An age's index here is its number everywhere (sim, GridData
// built-age bytes, saves), so ages may be appended but never reordered once saves exist.
[CreateAssetMenu(fileName = "AgeDatabase", menuName = "CityBuilder/Age Database")]
public sealed class AgeDatabase : ScriptableObject
{
    // The age whose rules match the pre-ages game (and v1 saves).
    public const string LegacyId = "industrial";

    [SerializeField] private List<AgeDefinition> m_Ages = new();

    public IReadOnlyList<AgeDefinition> Ages => m_Ages;
    public int Count => m_Ages.Count;
    public AgeDefinition this[int index] => m_Ages[index];

    // Index of the Industrial age, or -1 if the database has none.
    public int Legacy => IndexOf(LegacyId);

    public int IndexOf(string id)
    {
        for (int i = 0; i < m_Ages.Count; i++)
        {
            if (m_Ages[i] != null && m_Ages[i].Id == id) return i;
        }
        return -1;
    }

    public bool IsValidIndex(int index) => index >= 0 && index < m_Ages.Count;

    // Appends a message per problem to errors; true when there are none.
    public bool Validate(List<string> errors)
    {
        if (errors == null) throw new ArgumentNullException(nameof(errors));
        int before = errors.Count;

        if (m_Ages.Count == 0) errors.Add("AgeDatabase has no ages.");
        var ids = new HashSet<string>();
        for (int i = 0; i < m_Ages.Count; i++)
        {
            AgeDefinition age = m_Ages[i];
            if (age == null)
            {
                errors.Add($"Age #{i} is missing.");
                continue;
            }
            if (string.IsNullOrEmpty(age.Id)) errors.Add($"Age #{i} has no Id.");
            else if (!ids.Add(age.Id)) errors.Add($"Duplicate age Id '{age.Id}'.");
            if (age.MaxLevel < 1) errors.Add($"Age '{age.Id}': MaxLevel must be at least 1.");
            if (age.CapacityScale <= 0f) errors.Add($"Age '{age.Id}': CapacityScale must be positive.");

            if (i == 0) continue;
            AgeDefinition previous = m_Ages[i - 1];
            if (previous == null) continue;
            if (age.StartYear <= previous.StartYear)
                errors.Add($"Age '{age.Id}' starts in {age.StartYear}, not after '{previous.Id}' ({previous.StartYear}).");
            if (age.CapacityScale < previous.CapacityScale)
                errors.Add($"Age '{age.Id}' has a lower CapacityScale than '{previous.Id}'.");
        }
        return errors.Count == before;
    }

    internal void Init(params AgeDefinition[] ages)
    {
        m_Ages = new List<AgeDefinition>(ages);
    }
}
