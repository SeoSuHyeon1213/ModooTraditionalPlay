// 사방치기 이미지에 맞는 구역 Collider와 경계선 Trigger를 생성하고 Ray와 돌 중심의 구역을 판정한다.
using System.Collections.Generic;
using UnityEngine;

public class Map : MonoBehaviour
{
    public enum Region { None = 0, Tile1, Tile2, Tile3, Tile4, Tile5, Tile6, Tile7, Tile8, Sky }

    [Tooltip("흰 여백을 포함한 이미지 전체의 로컬 X 너비와 Z 길이")]
    [SerializeField] private Vector2 imageSize = new Vector2(2.5f, 3.5f);
    [Tooltip("놀이판 표면의 로컬 Y 좌표")]
    [SerializeField] private float surfaceY;
    [Tooltip("돌 중심이 표면에서 벗어나도 허용할 로컬 높이")]
    [SerializeField, Min(0.001f)] private float heightTolerance = 0.1f;
    [Tooltip("선 중심으로부터 칸 내부 방향으로 제외할 로컬 거리. 기본값은 선 두께의 절반 정도")]
    [SerializeField, Min(0f)] private float borderMargin = 0.007f;

    [Header("경계선 Trigger")]
    [Tooltip("그림의 선을 따라 경계선 판정용 Trigger를 만들 Layer. 하나만 선택하고 SabangStone의 Border Layers와 같게 둔다. 비우면 만들지 않는다.")]
    [SerializeField] private LayerMask borderLayer;
    [Tooltip("경계선 Trigger의 로컬 두께. 그림의 선 두께(Border Margin의 약 2배)에 맞춘다.")]
    [SerializeField, Min(0.001f)] private float borderThickness = 0.014f;
    [Tooltip("표면을 중심으로 경계선 Trigger가 위아래로 차지하는 로컬 높이. 날아가는 망은 닿지 않고 바닥의 망만 닿을 만큼 낮게 둔다.")]
    [SerializeField, Min(0.001f)] private float borderHeight = 0.02f;

    private readonly List<Vector2[]> polygons = new List<Vector2[]>();
    private readonly List<float[]> inverseEdgeLengths = new List<float[]>();
    private readonly Dictionary<Collider, int> colliderIndices = new Dictionary<Collider, int>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private GameObject regionsRoot;
    private Vector2 builtImageSize;

    private void Awake()
    {
        BuildPolygons();
        regionsRoot = new GameObject("Map Regions");
        regionsRoot.transform.SetParent(transform, false);
        for (int i = 0; i < polygons.Count; i++)
        {
            GameObject tile = new GameObject(((Region)(i + 1)).ToString());
            tile.layer = gameObject.layer;
            tile.transform.SetParent(regionsRoot.transform, false);
            MeshCollider collider = AddTriggerPrism(tile, polygons[i], surfaceY - 0.01f, surfaceY);
            colliderIndices.Add(collider, i);
        }
        BuildBorders();
    }

    // 모든 구역 외곽선을 선분으로 나눠 얇은 Trigger를 만든다. 이웃 칸이 공유하는 같은 선분은 한 번만 만든다.
    private void BuildBorders()
    {
        int bits = borderLayer.value;
        if (bits == 0) return;
        if ((bits & (bits - 1)) != 0)
        {
            Debug.LogError("Map: Border Layer는 하나만 선택하세요. 경계선 Trigger를 만들지 않았습니다.", this);
            return;
        }
        int layer = 0;
        while ((bits >> layer) != 1) layer++;

        GameObject bordersRoot = new GameObject("Map Borders") { layer = layer };
        bordersRoot.transform.SetParent(regionsRoot.transform, false);
        var built = new HashSet<(Vector2Int, Vector2Int)>();
        float half = borderThickness * 0.5f;
        foreach (Vector2[] polygon in polygons)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                if ((b - a).sqrMagnitude < 1e-8f || !built.Add(EdgeKey(a, b))) continue;
                // 선분 양 끝을 두께의 절반만큼 늘려 꺾이는 모서리에 틈이 없게 한다. 꼭짓점은 반시계 방향이다.
                Vector2 dir = (b - a).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x) * half;
                Vector2 start = a - dir * half, end = b + dir * half;
                GameObject border = new GameObject("Border") { layer = layer };
                border.transform.SetParent(bordersRoot.transform, false);
                AddTriggerPrism(border, new[] { start - side, end - side, end + side, start + side },
                    surfaceY - borderHeight * 0.5f, surfaceY + borderHeight * 0.5f);
            }
        }
    }

    private static (Vector2Int, Vector2Int) EdgeKey(Vector2 a, Vector2 b)
    {
        // 같은 이미지 좌표에서 계산한 꼭짓점이므로 반올림한 값으로 방향과 관계없이 같은 선분을 찾는다.
        Vector2Int p = Vector2Int.RoundToInt(a * 10000f), q = Vector2Int.RoundToInt(b * 10000f);
        return p.x < q.x || (p.x == q.x && p.y <= q.y) ? (p, q) : (q, p);
    }

    // Map의 비균일 Scale에서도 모양이 어긋나지 않도록 로컬 좌표 메쉬의 Convex Mesh Collider로 만든다.
    private MeshCollider AddTriggerPrism(GameObject owner, Vector2[] polygon, float bottom, float top)
    {
        Mesh mesh = CreatePrism(polygon, bottom, top);
        meshes.Add(mesh);
        MeshCollider collider = owner.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
        collider.isTrigger = true;
        return collider;
    }

    // 기존 Raycast 결과를 전달한다. 생성한 자식 Collider의 충돌만 받는다.
    public bool TryGetRegion(RaycastHit hit, out Region region)
    {
        region = Region.None;
        if (hit.collider == null || !colliderIndices.TryGetValue(hit.collider, out int index)) return false;
        Vector3 local = transform.InverseTransformPoint(hit.point);
        if (Mathf.Abs(local.y - surfaceY) > heightTolerance ||
            !Contains(index, new Vector2(local.x, local.z))) return false;
        region = (Region)(index + 1);
        return true;
    }

    // 돌 중심을 전달한다. 표면의 위아래 허용 범위 안에서만 판정한다.
    public bool TryGetRegion(Vector3 worldPoint, out Region region)
    {
        region = Region.None;
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        if (Mathf.Abs(local.y - surfaceY) > heightTolerance) return false;
        Vector2 point = new Vector2(local.x, local.z);
        for (int i = 0; i < polygons.Count; i++)
        {
            if (!Contains(i, point)) continue;
            region = (Region)(i + 1);
            return true;
        }
        return false;
    }

    // 놀이판 위의 점을 표면 높이로 내린다. 망 Collider 높이나 놓인 방향에 따라 판정이 바뀌지 않게 한다.
    public Vector3 ProjectToSurface(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        local.y = surfaceY;
        return transform.TransformPoint(local);
    }

    // 구역 외곽선을 놀이판 표면 높이의 월드 좌표로 전달한다. 자기 땅 표시 등 판정 밖의 표시에 사용한다.
    public bool TryGetRegionOutline(Region region, List<Vector3> worldPoints)
    {
        worldPoints.Clear();
        int index = (int)region - 1;
        if (index < 0 || index >= polygons.Count) return false;
        foreach (Vector2 point in polygons[index])
            worldPoints.Add(transform.TransformPoint(new Vector3(point.x, surfaceY, point.y)));
        return true;
    }

    private bool Contains(int polygonIndex, Vector2 point)
    {
        // 꼭짓점은 XZ 평면에서 반시계 방향이며 모든 변의 안쪽이어야 한다.
        Vector2[] polygon = polygons[polygonIndex];
        float[] edgeLengths = inverseEdgeLengths[polygonIndex];
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
            Vector2 offset = point - polygon[i];
            float distance = (edge.x * offset.y - edge.y * offset.x) * edgeLengths[i];
            if (distance <= borderMargin) return false;
        }
        return true;
    }

    private Vector2 Pixel(float x, float y)
    {
        // 제공 이미지의 비율. 원점은 이미지 중심, +Z는 하늘 방향이다.
        return new Vector2((x / 1334f - 0.5f) * imageSize.x,
            (0.5f - y / 1888f) * imageSize.y);
    }

    private void BuildPolygons()
    {
        polygons.Clear();
        inverseEdgeLengths.Clear();
        Vector2 bl = Pixel(182, 1487), br = Pixel(1151, 1487);
        Vector2 tl = Pixel(182, 763), tr = Pixel(1151, 763), center = Pixel(667, 1125);
        polygons.Add(new[] { Pixel(182, 1849), Pixel(667, 1849), Pixel(667, 1487), bl });
        polygons.Add(new[] { Pixel(667, 1849), Pixel(1151, 1849), br, Pixel(667, 1487) });
        polygons.Add(new[] { bl, br, center });
        polygons.Add(new[] { bl, center, tl });
        polygons.Add(new[] { center, br, tr });
        polygons.Add(new[] { center, tr, tl });
        polygons.Add(new[] { tl, Pixel(667, 763), Pixel(667, 401), Pixel(182, 401) });
        polygons.Add(new[] { Pixel(667, 763), tr, Pixel(1151, 401), Pixel(667, 401) });
        Vector2[] sky = new Vector2[33];
        for (int i = 0; i < sky.Length; i++)
        {
            float angle = Mathf.PI * i / (sky.Length - 1);
            sky[i] = Pixel(666.5f + 484.5f * Mathf.Cos(angle), 401f - 362f * Mathf.Sin(angle));
        }
        polygons.Add(sky);
        for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
        {
            Vector2[] polygon = polygons[polygonIndex];
            float[] lengths = new float[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                float length = (polygon[(i + 1) % polygon.Length] - polygon[i]).magnitude;
                lengths[i] = length > Mathf.Epsilon ? 1f / length : 0f;
            }
            inverseEdgeLengths.Add(lengths);
        }
        builtImageSize = imageSize;
    }

    private Mesh CreatePrism(Vector2[] polygon, float bottom, float top)
    {
        int n = polygon.Length;
        Vector3[] vertices = new Vector3[n * 2];
        List<int> triangles = new List<int>(n * 12 - 12);
        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(polygon[i].x, bottom, polygon[i].y);
            vertices[i + n] = new Vector3(polygon[i].x, top, polygon[i].y);
            int next = (i + 1) % n;
            triangles.Add(i);
            triangles.Add(i + n);
            triangles.Add(next + n);
            triangles.Add(i);
            triangles.Add(next + n);
            triangles.Add(next);
        }
        for (int i = 1; i < n - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(n);
            triangles.Add(n + i + 1);
            triangles.Add(n + i);
        }
        Mesh mesh = new Mesh { name = "Map Region", vertices = vertices, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying && (polygons.Count == 0 || !builtImageSize.Equals(imageSize)))
            BuildPolygons();
        Gizmos.matrix = transform.localToWorldMatrix;
        for (int i = 0; i < polygons.Count; i++)
        {
            Gizmos.color = Color.HSVToRGB(i / 9f, 0.8f, 1f);
            Vector2[] polygon = polygons[i];
            for (int j = 0; j < polygon.Length; j++)
            {
                Vector2 a = polygon[j], b = polygon[(j + 1) % polygon.Length];
                Gizmos.DrawLine(new Vector3(a.x, surfaceY, a.y), new Vector3(b.x, surfaceY, b.y));
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDestroy()
    {
        if (regionsRoot != null) Destroy(regionsRoot);
        foreach (Mesh mesh in meshes) Destroy(mesh);
    }
}
