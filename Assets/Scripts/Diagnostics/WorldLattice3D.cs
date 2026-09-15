using UnityEngine;
using UnityEngine.Rendering;

namespace OverTheHorizon.Diagnostics
{
    /// <summary>
    /// 플레이어 주변에 월드 고정형 3D 격자(lattice)를 그린다.
    /// 실제 위치를 매 프레임 따라가지 않고 Cell 단위로 Snap하므로
    /// 전진/횡/수직/대각선 속도를 상대운동으로 읽을 수 있다.
    /// 별도 Material/Shader asset 불필요.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldLattice3D : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Transform target;

        [Header("Lattice")]
        [Min(0.1f)]
        [SerializeField] private float cellSize = 5f;
        [Min(1)]
        [SerializeField] private int halfCellCount = 8;
        [Min(1)]
        [SerializeField] private int majorLineEvery = 5;

        [Header("Colors")]
        [SerializeField] private Color minorColor = new Color(0.22f, 0.42f, 0.50f, 0.20f);
        [SerializeField] private Color majorColor = new Color(0.65f, 0.78f, 0.85f, 0.48f);
        [SerializeField] private Color xAxisColor = new Color(1f, 0.22f, 0.22f, 0.95f);
        [SerializeField] private Color yAxisColor = new Color(0.25f, 1f, 0.35f, 0.95f);
        [SerializeField] private Color zAxisColor = new Color(0.25f, 0.55f, 1f, 0.95f);

        [Header("Rendering")]
        [SerializeField] private bool drawMinorLines = true;
        [SerializeField] private bool followTargetOnY = true;

        private Mesh latticeMesh;
        private Material latticeMaterial;

        private float cachedCellSize;
        private int cachedHalfCellCount;
        private int cachedMajorLineEvery;
        private bool cachedDrawMinorLines;

        private void Awake()
        {
            CreateMaterial();
            RebuildMesh();
        }

        private void OnEnable()
        {
            if (latticeMaterial == null)
                CreateMaterial();
            if (latticeMesh == null)
                RebuildMesh();
        }

        private void LateUpdate()
        {
            if (NeedsRebuild())
                RebuildMesh();

            DrawLattice();
        }

        private void CreateMaterial()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogError("WorldLattice3D: 디버그 선 렌더링용 Shader를 찾지 못했습니다.", this);
                enabled = false;
                return;
            }

            latticeMaterial = new Material(shader)
            {
                name = "Runtime 3D World Lattice",
                hideFlags = HideFlags.HideAndDontSave
            };

            if (latticeMaterial.HasProperty("_SrcBlend"))
                latticeMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (latticeMaterial.HasProperty("_DstBlend"))
                latticeMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (latticeMaterial.HasProperty("_ZWrite"))
                latticeMaterial.SetInt("_ZWrite", 0);
            if (latticeMaterial.HasProperty("_Cull"))
                latticeMaterial.SetInt("_Cull", (int)CullMode.Off);

            latticeMaterial.renderQueue = (int)RenderQueue.Transparent;
        }

        private void RebuildMesh()
        {
            if (latticeMesh != null)
                Destroy(latticeMesh);

            int n = halfCellCount * 2 + 1;
            int maxLines = 3 * n * n;

            Vector3[] vertices = new Vector3[maxLines * 2];
            Color[] colors = new Color[maxLines * 2];
            int[] indices = new int[maxLines * 2];

            float extent = halfCellCount * cellSize;
            int v = 0;

            // X 방향 선: 고정 Y/Z 조합마다 1개
            for (int yi = -halfCellCount; yi <= halfCellCount; yi++)
            {
                for (int zi = -halfCellCount; zi <= halfCellCount; zi++)
                {
                    Color color = EvaluateLineColor(Axis.X, yi, zi);
                    if (!ShouldDraw(color))
                        continue;

                    AddLine(
                        new Vector3(-extent, yi * cellSize, zi * cellSize),
                        new Vector3(extent, yi * cellSize, zi * cellSize),
                        color,
                        vertices,
                        colors,
                        indices,
                        ref v);
                }
            }

            // Y 방향 선: 고정 X/Z 조합마다 1개
            for (int xi = -halfCellCount; xi <= halfCellCount; xi++)
            {
                for (int zi = -halfCellCount; zi <= halfCellCount; zi++)
                {
                    Color color = EvaluateLineColor(Axis.Y, xi, zi);
                    if (!ShouldDraw(color))
                        continue;

                    AddLine(
                        new Vector3(xi * cellSize, -extent, zi * cellSize),
                        new Vector3(xi * cellSize, extent, zi * cellSize),
                        color,
                        vertices,
                        colors,
                        indices,
                        ref v);
                }
            }

            // Z 방향 선: 고정 X/Y 조합마다 1개
            for (int xi = -halfCellCount; xi <= halfCellCount; xi++)
            {
                for (int yi = -halfCellCount; yi <= halfCellCount; yi++)
                {
                    Color color = EvaluateLineColor(Axis.Z, xi, yi);
                    if (!ShouldDraw(color))
                        continue;

                    AddLine(
                        new Vector3(xi * cellSize, yi * cellSize, -extent),
                        new Vector3(xi * cellSize, yi * cellSize, extent),
                        color,
                        vertices,
                        colors,
                        indices,
                        ref v);
                }
            }

            System.Array.Resize(ref vertices, v);
            System.Array.Resize(ref colors, v);
            System.Array.Resize(ref indices, v);

            latticeMesh = new Mesh
            {
                name = "Runtime 3D World Lattice Mesh"
            };

            if (v > 65535)
                latticeMesh.indexFormat = IndexFormat.UInt32;

            latticeMesh.vertices = vertices;
            latticeMesh.colors = colors;
            latticeMesh.SetIndices(indices, MeshTopology.Lines, 0, false);

            float size = extent * 2f + cellSize * 2f;
            latticeMesh.bounds = new Bounds(Vector3.zero, Vector3.one * size);

            cachedCellSize = cellSize;
            cachedHalfCellCount = halfCellCount;
            cachedMajorLineEvery = majorLineEvery;
            cachedDrawMinorLines = drawMinorLines;
        }

        private enum Axis
        {
            X,
            Y,
            Z
        }

        private Color EvaluateLineColor(Axis axis, int first, int second)
        {
            // 두 횡좌표가 모두 0이면 월드 원점을 지나는 실제 X/Y/Z 축이다.
            if (first == 0 && second == 0)
            {
                return axis switch
                {
                    Axis.X => xAxisColor,
                    Axis.Y => yAxisColor,
                    _ => zAxisColor
                };
            }

            bool major =
                first % majorLineEvery == 0
                && second % majorLineEvery == 0;

            return major ? majorColor : minorColor;
        }

        private bool ShouldDraw(Color color)
        {
            if (drawMinorLines)
                return true;

            // Minor Color인 경우만 생략한다.
            return color != minorColor;
        }

        private static void AddLine(
            Vector3 start,
            Vector3 end,
            Color color,
            Vector3[] vertices,
            Color[] colors,
            int[] indices,
            ref int vertexIndex)
        {
            vertices[vertexIndex] = start;
            vertices[vertexIndex + 1] = end;
            colors[vertexIndex] = color;
            colors[vertexIndex + 1] = color;
            indices[vertexIndex] = vertexIndex;
            indices[vertexIndex + 1] = vertexIndex + 1;
            vertexIndex += 2;
        }

        private void DrawLattice()
        {
            if (latticeMesh == null || latticeMaterial == null)
                return;

            Vector3 center = Vector3.zero;

            if (target != null)
            {
                center.x = Mathf.Floor(target.position.x / cellSize) * cellSize;
                center.z = Mathf.Floor(target.position.z / cellSize) * cellSize;

                if (followTargetOnY)
                    center.y = Mathf.Floor(target.position.y / cellSize) * cellSize;
            }

            Graphics.DrawMesh(
                latticeMesh,
                Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                latticeMaterial,
                gameObject.layer,
                null,
                0,
                null,
                ShadowCastingMode.Off,
                false,
                null,
                false);
        }

        private bool NeedsRebuild()
        {
            return !Mathf.Approximately(cachedCellSize, cellSize)
                   || cachedHalfCellCount != halfCellCount
                   || cachedMajorLineEvery != majorLineEvery
                   || cachedDrawMinorLines != drawMinorLines;
        }

        private void OnDestroy()
        {
            if (latticeMesh != null)
                Destroy(latticeMesh);
            if (latticeMaterial != null)
                Destroy(latticeMaterial);
        }
    }
}
