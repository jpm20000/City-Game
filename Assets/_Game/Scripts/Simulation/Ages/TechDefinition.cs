using System;
using UnityEngine;

// One technology (GamePlan §12, M11). Age = index into the AgeDatabase; a tech can be researched
// once the city has reached its age and researched its prerequisites (same or earlier ages).
[CreateAssetMenu(fileName = "Tech", menuName = "CityBuilder/Tech")]
public sealed class TechDefinition : ScriptableObject
{
    [SerializeField] private string m_Id = "";
    [SerializeField] private string m_DisplayName = "";
    [SerializeField, TextArea] private string m_Description = "";
    [SerializeField] private int m_Age;
    [Tooltip("Research points.")]
    [SerializeField] private float m_Cost = 100f;
    [SerializeField] private TechDefinition[] m_Prerequisites = Array.Empty<TechDefinition>();
    [SerializeField] private TechEffect[] m_Effects = Array.Empty<TechEffect>();

    public string Id => m_Id;
    public string DisplayName => string.IsNullOrEmpty(m_DisplayName) ? m_Id : m_DisplayName;
    public string Description => m_Description;
    public int Age => m_Age;
    public float Cost => m_Cost;
    public TechDefinition[] Prerequisites => m_Prerequisites ?? Array.Empty<TechDefinition>();
    public TechEffect[] Effects => m_Effects ?? Array.Empty<TechEffect>();

    internal void Init(string id, int age, float cost, TechDefinition[] prerequisites = null, TechEffect[] effects = null)
    {
        m_Id = id;
        m_DisplayName = id;
        m_Age = age;
        m_Cost = cost;
        m_Prerequisites = prerequisites ?? Array.Empty<TechDefinition>();
        m_Effects = effects ?? Array.Empty<TechEffect>();
    }
}
