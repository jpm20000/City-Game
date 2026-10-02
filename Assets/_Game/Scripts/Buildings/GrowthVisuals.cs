using System.Collections.Generic;
using UnityEngine;

// Mirrors grown zone cells (GridData building levels) as tinted cubes whose height tracks level.
public sealed class GrowthVisuals : MonoBehaviour
{
    private const int k_BuildingsLayer = 9;
    private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] private Material m_Material;
    [SerializeField] private float m_HeightPerLevel = 0.6f;
    [SerializeField] private float m_Footprint = 0.8f;
    [SerializeField] private Color m_ResidentialColor = new Color(0.40f, 0.75f, 0.35f);
    [SerializeField] private Color m_CommercialColor = new Color(0.30f, 0.55f, 0.90f);
    [SerializeField] private Color m_IndustrialColor = new Color(0.90f, 0.75f, 0.25f);

    private readonly Dictionary<Vector2Int, Transform> m_Cubes = new();
    private GridData m_Grid;
    private MaterialPropertyBlock m_Block;

    public void Init(GridData grid)
    {
        m_Grid = grid;
        m_Block = new MaterialPropertyBlock();
        m_Grid.OnCellChanged += SyncCell;
    }

    private void OnDestroy()
    {
        if (m_Grid != null) m_Grid.OnCellChanged -= SyncCell;
    }

    private void SyncCell(Vector2Int cell)
    {
        int level = m_Grid.GetBuildingLevel(cell);
        m_Cubes.TryGetValue(cell, out Transform cube);

        if (level == 0)
        {
            if (cube != null) Destroy(cube.gameObject);
            m_Cubes.Remove(cell);
            return;
        }

        if (cube == null)
        {
            cube = CreateCube(cell);
            m_Cubes[cell] = cube;
        }

        float height = level * m_HeightPerLevel;
        cube.localScale = new Vector3(m_Footprint, height, m_Footprint);
        cube.position = new Vector3(cell.x + 0.5f, height * 0.5f, cell.y + 0.5f);

        m_Block.SetColor(s_BaseColorId, ZoneColor(m_Grid.GetZone(cell)));
        cube.GetComponent<MeshRenderer>().SetPropertyBlock(m_Block);
    }

    private Transform CreateCube(Vector2Int cell)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Grown_{cell.x}_{cell.y}";
        go.layer = k_BuildingsLayer;
        go.transform.SetParent(transform, false);
        if (m_Material != null) go.GetComponent<MeshRenderer>().sharedMaterial = m_Material;
        return go.transform;
    }

    private Color ZoneColor(ZoneType zone)
    {
        switch (zone)
        {
            case ZoneType.Residential: return m_ResidentialColor;
            case ZoneType.Commercial: return m_CommercialColor;
            case ZoneType.Industrial: return m_IndustrialColor;
            default: return Color.gray;
        }
    }
}
