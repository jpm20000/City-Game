using UnityEngine;

public sealed class BuildingInstance : MonoBehaviour
{
    private static int s_NextId = 1;

    // Optional child holding decorations (e.g. park trees) authored in world units. The root is
    // scaled to footprint x height, so the decor is counter-scaled and placed on the top surface.
    [SerializeField] private Transform m_Decor;

    public BuildingDefinition Definition { get; private set; }
    public Vector2Int Origin { get; private set; }
    public int Rotation { get; private set; }
    public int Level { get; private set; } = 1;
    public int OccupantId { get; private set; }

    private GridData m_Grid;

    public bool Init(GridData grid, BuildingDefinition definition, Vector2Int origin, int rotation)
    {
        if (grid == null || definition == null) return false;
        if (!grid.Occupy(origin, definition.Size, rotation, s_NextId)) return false;

        m_Grid = grid;
        Definition = definition;
        Origin = origin;
        Rotation = rotation;
        OccupantId = s_NextId++;

        ApplyTransform();
        return true;
    }

    public void Demolish()
    {
        if (m_Grid != null && Definition != null)
        {
            m_Grid.Release(Origin, Definition.Size, Rotation);
        }
        m_Grid = null;
    }

    private void ApplyTransform()
    {
        Vector2Int size = Definition.Size;
        int effectiveWidth = (Rotation & 1) == 0 ? size.x : size.y;
        int effectiveDepth = (Rotation & 1) == 0 ? size.y : size.x;
        float height = Definition.Height * Level;

        transform.position = new Vector3(
            Origin.x + effectiveWidth * 0.5f,
            height * 0.5f,
            Origin.y + effectiveDepth * 0.5f);
        // Rotation swaps the effective dims back into local X/Z, so scale by the unrotated size.
        transform.localScale = new Vector3(size.x, height, size.y);
        transform.rotation = Quaternion.Euler(0f, Rotation * 90f, 0f);

        if (m_Decor != null)
        {
            m_Decor.localPosition = new Vector3(0f, 0.5f, 0f);
            m_Decor.localScale = new Vector3(1f / size.x, 1f / height, 1f / size.y);
        }
    }
}
