// 땅따먹기로 얻은 칸을 놀이판 위에 칸 모양 그대로 채워 표시한다.
using System.Collections.Generic;
using UnityEngine;

public class SabangLandMarker : MonoBehaviour
{
    [SerializeField] private Map map;
    [Tooltip("자기 땅 표시용 오브젝트의 Mesh Filter. 같은 오브젝트의 Mesh Renderer에 색상이나 문양 Material을 지정하고 Collider는 두지 않는다.")]
    [SerializeField] private MeshFilter landFill;
    [Tooltip("표시가 놀이판에 가려지지 않도록 표면 위로 띄우는 거리(m)")]
    [SerializeField, Min(0f)] private float surfaceOffset = 0.005f;

    public int ClaimedTile { get; private set; }

    private Mesh mesh;
    private MeshRenderer fillRenderer;
    private readonly List<Vector3> outline = new List<Vector3>();

    private void Start()
    {
        if (ClaimedTile == 0) Clear();
    }

    public bool ValidateConfiguration()
    {
        if (map == null) return ConfigurationError("Map을 연결하세요.");
        if (landFill == null) return ConfigurationError("자기 땅 표시용 Mesh Filter를 Land Fill에 연결하세요.");
        if (fillRenderer == null) fillRenderer = landFill.GetComponent<MeshRenderer>();
        if (fillRenderer == null || fillRenderer.sharedMaterial == null)
            return ConfigurationError("Land Fill과 같은 오브젝트에 Mesh Renderer를 추가하고 표시 Material을 지정하세요.");
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("SabangLandMarker: " + message, this);
        return false;
    }

    public bool Show(int tile)
    {
        if (!ValidateConfiguration()) return false;
        if (tile < 1 || tile > 8 || !map.TryGetRegionOutline((Map.Region)tile, outline) || outline.Count < 3)
            return ConfigurationError($"{tile}번 칸의 외곽선을 찾지 못했습니다.");

        int count = outline.Count;
        Vector3 lift = map.transform.up * surfaceOffset;
        var vertices = new Vector3[count];
        var uvs = new Vector2[count];
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < count; i++)
        {
            vertices[i] = landFill.transform.InverseTransformPoint(outline[i] + lift);
            Vector3 onMap = map.transform.InverseTransformPoint(outline[i]);
            uvs[i] = new Vector2(onMap.x, onMap.z);
            min = Vector2.Min(min, uvs[i]);
            max = Vector2.Max(max, uvs[i]);
        }
        // 문양 Material이 칸 크기에 맞게 한 번 덮이도록 칸 범위를 0~1로 맞춘다.
        Vector2 size = Vector2.Max(max - min, new Vector2(0.0001f, 0.0001f));
        for (int i = 0; i < count; i++)
            uvs[i] = new Vector2((uvs[i].x - min.x) / size.x, (uvs[i].y - min.y) / size.y);

        // 1~8번 칸은 볼록 다각형이므로 부채꼴로 나눈다. Map의 윗면과 같은 감김 순서로 위쪽을 앞면으로 한다.
        var triangles = new int[(count - 2) * 3];
        for (int i = 1; i < count - 1; i++)
        {
            int t = (i - 1) * 3;
            triangles[t] = 0;
            triangles[t + 1] = i + 1;
            triangles[t + 2] = i;
        }

        if (mesh == null) mesh = new Mesh { name = "Sabang Claimed Land" };
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        landFill.sharedMesh = mesh;
        fillRenderer.enabled = true;
        ClaimedTile = tile;
        return true;
    }

    public void Clear()
    {
        ClaimedTile = 0;
        if (fillRenderer == null && landFill != null) fillRenderer = landFill.GetComponent<MeshRenderer>();
        if (fillRenderer != null) fillRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
