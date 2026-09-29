using UnityEngine;

public sealed class CameraSortAxis : MonoBehaviour
{
    [SerializeField]
    private Vector3 m_SortAxis = new Vector3(0f, 1f, 0f);

    private void Awake()
    {
        var cam = GetComponent<Camera>();
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = m_SortAxis;
    }
}
