using System;
using UnityEngine;

// One option of a random event (M17). The price is Cost + CostPerResident x population; instant results are the
// Reward (money) and ResearchPoints, lasting ones are Effects folded into TechModifiers for Days days (happiness
// effects count as the separate Events happiness term). The last choice of an event is always free.
[Serializable]
public struct EventChoice
{
    public string Label;
    public string Description;          // what it does, in a line, for the popup
    public float Cost;
    public float CostPerResident;
    public float Reward;
    public float ResearchPoints;
    public int Days;
    public TechEffect[] Effects;

    public float PriceAt(int population) => Cost + CostPerResident * Mathf.Max(0, population);
    public bool IsFree => Cost <= 0f && CostPerResident <= 0f;
    public bool HasLastingEffects => Days > 0 && Effects != null && Effects.Length > 0;
}

// A random event the city can be offered (M17): a title, a text and two or three choices. Eligible while the city's age
// is within MinAge..MaxAge (age indices), it has MinPopulation residents and RequiredTech (if any) is researched.
// Weight is its share among the eligible ones.
[CreateAssetMenu(fileName = "Event", menuName = "CityBuilder/Event")]
public sealed class EventDefinition : ScriptableObject
{
    [SerializeField] private string m_Id = "";
    [SerializeField] private string m_Title = "";
    [SerializeField, TextArea] private string m_Text = "";
    [SerializeField] private int m_MinAge;
    [SerializeField] private int m_MaxAge = 3;
    [SerializeField] private int m_MinPopulation;
    [SerializeField] private TechDefinition m_RequiredTech;
    [SerializeField] private float m_Weight = 1f;
    [SerializeField] private EventChoice[] m_Choices = Array.Empty<EventChoice>();

    public string Id => m_Id;
    public string Title => string.IsNullOrEmpty(m_Title) ? m_Id : m_Title;
    public string Text => m_Text;
    public int MinAge => m_MinAge;
    public int MaxAge => m_MaxAge;
    public int MinPopulation => m_MinPopulation;
    public TechDefinition RequiredTech => m_RequiredTech;
    public float Weight => m_Weight;
    public EventChoice[] Choices => m_Choices ?? Array.Empty<EventChoice>();

    internal void Init(string id, int minAge, int maxAge, int minPopulation, TechDefinition requiredTech, float weight,
        params EventChoice[] choices)
    {
        m_Id = id;
        m_Title = id;
        m_MinAge = minAge;
        m_MaxAge = maxAge;
        m_MinPopulation = minPopulation;
        m_RequiredTech = requiredTech;
        m_Weight = weight;
        m_Choices = choices ?? Array.Empty<EventChoice>();
    }
}
