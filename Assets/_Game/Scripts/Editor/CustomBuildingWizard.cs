using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// M25b: CityBuilder > Add Custom Building... A form over CustomBuildingBuilder: pick the model, name it, set cost and
// effects, press Create. Problems are listed in the window; nothing is written when the check fails. Docs/CustomAssets.md.
public sealed class CustomBuildingWizard : EditorWindow
{
    private readonly CustomBuildingRequest m_Request = new();
    private readonly List<string> m_Messages = new();
    private bool m_LastOk;
    private string[] m_Techs = System.Array.Empty<string>();
    private Vector2 m_Scroll;

    [MenuItem("CityBuilder/Add Custom Building...")]
    public static void Open()
    {
        var window = GetWindow<CustomBuildingWizard>(true, "Add Custom Building");
        window.minSize = new Vector2(420f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        m_Techs = new[] { "(none: available from the start)" }
            .Concat(CustomBuildingBuilder.AllTechIds().OrderBy(t => t)).ToArray();
    }

    private void OnGUI()
    {
        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
        EditorGUILayout.HelpBox("Adds a placeable building: it appears in the toolbar groups once its tech is researched. "
            + "Read Docs/CustomAssets.md for the art rules.", MessageType.Info);

        m_Request.DisplayName = EditorGUILayout.TextField("Display name", m_Request.DisplayName);
        m_Request.Id = EditorGUILayout.TextField(new GUIContent("Id", "Saved in cities; never change it later. Blank = made from the name."), m_Request.Id);
        string shownId = CustomBuildingBuilder.NormalizeId(string.IsNullOrWhiteSpace(m_Request.Id) ? m_Request.DisplayName : m_Request.Id);
        EditorGUILayout.LabelField(" ", shownId.Length > 0 ? $"will be saved as '{shownId}'" : "");

        EditorGUILayout.Space();
        m_Request.Source = (GameObject)EditorGUILayout.ObjectField("Model", m_Request.Source, typeof(GameObject), true);
        m_Request.WrapModel = EditorGUILayout.ToggleLeft(
            "Fit the model to the unit box (a new game prefab is made around it)", m_Request.WrapModel);
        if (!m_Request.WrapModel) EditorGUILayout.HelpBox("The model must already be a game prefab: a unit box with a BuildingInstance on the root.", MessageType.None);

        EditorGUILayout.Space();
        m_Request.Category = (BuildingCategory)EditorGUILayout.EnumPopup("Category", m_Request.Category);
        if (m_Request.Category == BuildingCategory.Zone || m_Request.Category == BuildingCategory.Road) m_Request.Category = BuildingCategory.Service;
        m_Request.Size = EditorGUILayout.Vector2IntField("Footprint (cells)", m_Request.Size);
        m_Request.Height = EditorGUILayout.FloatField(new GUIContent("Height (cells)", "How tall the model stands."), m_Request.Height);
        m_Request.Cost = EditorGUILayout.IntField("Cost", m_Request.Cost);
        m_Request.UpkeepPerDay = EditorGUILayout.FloatField("Upkeep per day", m_Request.UpkeepPerDay);
        m_Request.HousingCapacity = EditorGUILayout.IntField("Housing capacity", m_Request.HousingCapacity);
        m_Request.JobsProvided = EditorGUILayout.IntField("Jobs provided", m_Request.JobsProvided);
        m_Request.HappinessEffect = EditorGUILayout.FloatField("Happiness effect", m_Request.HappinessEffect);

        int techIndex = Mathf.Max(0, System.Array.IndexOf(m_Techs, m_Request.RequiredTech));
        techIndex = EditorGUILayout.Popup("Required tech", techIndex, m_Techs);
        m_Request.RequiredTech = techIndex == 0 ? "" : m_Techs[techIndex];
        m_Request.Icon = (Sprite)EditorGUILayout.ObjectField("Icon (optional)", m_Request.Icon, typeof(Sprite), false);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(m_Request.Source == null || shownId.Length == 0))
        {
            if (GUILayout.Button("Create / update", GUILayout.Height(28f))) Run();
        }

        foreach (string message in m_Messages)
        {
            EditorGUILayout.HelpBox(message, m_LastOk ? MessageType.Info : MessageType.Error);
        }
        EditorGUILayout.EndScrollView();
    }

    private void Run()
    {
        if (string.IsNullOrWhiteSpace(m_Request.Id)) m_Request.Id = m_Request.DisplayName;
        m_Messages.Clear();
        BuildingDefinition def = CustomBuildingBuilder.Create(m_Request, CustomBuildingBuilder.AllTechIds(), m_Messages);
        m_LastOk = def != null;
        if (def != null)
        {
            m_Messages.Clear();
            m_Messages.Add($"'{def.Id}' is in the BuildingDatabase ({AssetDatabase.GetAssetPath(def)}). Commit the new assets and their .meta files.");
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
        }
    }
}
