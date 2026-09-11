// 사방치기 이미지에 맞는 구역 Collider를 생성하고 Ray와 돌 중심의 구역을 판정한다.
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

    private readonly List<Vector2[]> polygons = new List<Vector2[]>();
    private readonly List<MeshCollider> regionColliders = new List<MeshCollider>();
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
            Mesh mesh = CreatePrism(polygons[i]);
            meshes.Add(mesh);
            MeshCollider collider = tile.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
            collider.isTrigger = true;
            regionColliders.Add(collider);
        }
    }

    // 기존 Raycast 결과를 전달한다. 생성한 자식 Collider의 충돌만 받는다.
    public bool TryGetRegion(RaycastHit hit, out Region region)
    {
        region = Region.None;
        int index = regionColliders.IndexOf(hit.collider as MeshCollider);
        if (index < 0) return false;
        Vector3 local = transform.InverseTransformPoint(hit.point);
        if (Mathf.Abs(local.y - surfaceY) > heightTolerance ||
            !Contains(polygons[index], new Vector2(local.x, local.z))) return false;
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
            if (!Contains(polygons[i], point)) continue;
            region = (Region)(i + 1);
            return true;
        }
        return false;
    }

    private bool Contains(Vector2[] polygon, Vector2 point)
    {
        // 꼭짓점은 XZ 평면에서 반시계 방향이며 모든 변의 안쪽이어야 한다.
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
            Vector2 offset = point - polygon[i];
            float distance = (edge.x * offset.y - edge.y * offset.x) / edge.magnitude;
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
        builtImageSize = imageSize;
    }

    private Mesh CreatePrism(Vector2[] polygon)
    {
        int n = polygon.Length;
        Vector3[] vertices = new Vector3[n * 2];
        List<int> triangles = new List<int>();
        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(polygon[i].x, surfaceY - 0.01f, polygon[i].y);
            vertices[i + n] = new Vector3(polygon[i].x, surfaceY, polygon[i].y);
            int next = (i + 1) % n;
            triangles.AddRange(new[] { i, i + n, next + n, i, next + n, next });
        }
        for (int i = 1; i < n - 1; i++)
            triangles.AddRange(new[] { 0, i, i + 1, n, n + i + 1, n + i });
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
