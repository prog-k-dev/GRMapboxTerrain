using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mapbox.Unity.MeshGeneration.Data;
using UnityEngine;

public class GRTileMesh : MonoBehaviour
{
    private static readonly Color32 DEFAULT_COLOR = new Color32(0, 0, 0, 0);

    // 高度レンダリング用カメラ
    [SerializeField]
    private Camera _camera = null;

    // 地面メッシュ
    [SerializeField]
    private MeshFilter _groundMeshFilter = null;

    // 建物メッシュ
    [SerializeField]
    private MeshFilter _featureMeshFilter = null;

    // 解像度
    public int Resolution
    {
        get;
        private set;
    }

    // 高さ範囲：[0]min、[1]max
    public Vector2 HeightRange
    {
        get => _heightRange;
    }

    // 高さ：(max - min)
    public float Height
    {
        get => _heightRange[1] - _heightRange[0];
    }

    private MeshRenderer _groundMeshRenderer = null;
    private Mesh _groundMesh = null;

    private MeshRenderer _featureMeshRenderer = null;
    private Mesh _featureMesh = null;


    private Vector2 _heightRange;
    private Vector2Int _textureSize;
    private RenderTexture _renderTexture = null;

    private UnityTile _unityTile = null;
    private List<VectorEntity> _vectorEntities = new List<VectorEntity>();

    // タイルセット
    public bool SetTile(UnityTile tile)
    {
        if (!SetupCamera(tile))
        {
            return false;
        }

        _heightRange.Set(float.MaxValue, -float.MaxValue);

        _groundMeshRenderer = _groundMeshFilter.GetComponent<MeshRenderer>();
        _groundMeshFilter.sharedMesh = (_groundMesh = new Mesh());

        _featureMeshRenderer = _featureMeshFilter.GetComponent<MeshRenderer>();
        _featureMeshFilter.sharedMesh = (_featureMesh = new Mesh());

        // タイルメッシュ追加
        if (!AddMesh(_groundMesh, _groundMeshFilter.transform, tile.MeshFilter.sharedMesh, tile.transform))
        {
            return false;
        }
        _unityTile = tile;

        return true;
    }

    // 建物メッシュ追加
    public bool AddMesh(VectorEntity vectorEntity)
    {
        if (!AddMesh(_featureMesh, _featureMeshFilter.transform, vectorEntity.Mesh, vectorEntity.Transform))
        {
            return false;
        }
        _vectorEntities.Add(vectorEntity);

        return true;
    }

    // メッシュ表示
    public void ShowMesh(bool show = true)
    {
        // タイルメッシュ
        if (_unityTile != null)
        {
            _unityTile.MeshRenderer.enabled = show;
        }

        // 建物メッシュ
        foreach (var vectorEntity in _vectorEntities)
        {
            vectorEntity.MeshRenderer.enabled = show;
        }
    }

    // 地面高さ取得
    public float[,] GetGroundHeight()
    {
        float[,] heights = new float[_textureSize.x, _textureSize.y];

        _groundMeshRenderer.enabled = true;
        {
            UpdateHeight(_groundMeshFilter);

            if (!GetHeight(heights))
            {
                heights = null;
            }
        }
        _groundMeshRenderer.enabled = false;

        return heights;
    }

    // 建物高さ取得
    public float[,] GetFeatureHeight()
    {
        float[,] heights = new float[_textureSize.x, _textureSize.y];

        _featureMeshRenderer.enabled = true;
        {
            UpdateHeight(_featureMeshFilter);

            if (!GetHeight(heights))
            {
                heights = null;
            }
        }
        _featureMeshRenderer.enabled = false;

        return heights;
    }

    // カメラをセットアップ
    private bool SetupCamera(UnityTile tile)
    {
        Resolution = Mathf.CeilToInt(Mathf.Sqrt(tile.HeightData.Length));
        _textureSize = new Vector2Int(Resolution, Resolution);

        _renderTexture = new RenderTexture(_textureSize.x, _textureSize.y, 0, RenderTextureFormat.ARGB32);
        if (!_renderTexture.Create())
        {
            Debug.LogError("Error");
            return false;
        }
        _camera.targetTexture = _renderTexture;

        return true;
    }

    // メッシュ追加
    private bool AddMesh(Mesh targetMesh, Transform targetTransform, Mesh mesh, Transform meshParent)
    {
        // NOTE: targetMeshにmeshの頂点、ポリゴンを追加する

        List<Vector3> allVertices;
        List<Color32> allColors;
        List<int> allTriangles;
        {
            targetMesh.GetVertices(allVertices = new List<Vector3>());
            targetMesh.GetColors(allColors = new List<Color32>());
            allTriangles = new List<int>(targetMesh.GetTriangles(0));

            var vertexCount = allVertices.Count;
            var vertices = mesh.vertices;
            for (int i = 0, count = mesh.vertexCount; i < count; ++i)
            {
                var world = meshParent.TransformPoint(vertices[i]);
                var local = targetTransform.InverseTransformPoint(world);
                allVertices.Add(local);

                // colorは高さを表す
                // この時点では無効値（Alpha＝ゼロ）をセット
                allColors.Add(DEFAULT_COLOR);

                // 高さ範囲更新
                _heightRange[0] = Mathf.Min(_heightRange[0], world.y);
                _heightRange[1] = Mathf.Max(_heightRange[1], world.y);
            }

            var triangles = mesh.triangles;
            for (int i = 0, count = triangles.Length; i < count; ++i)
            {
                allTriangles.Add(triangles[i] + vertexCount);
            }
        }
        targetMesh.SetVertices(allVertices);
        targetMesh.SetColors(allColors);
        targetMesh.SetTriangles(allTriangles, 0);

        return true;
    }

    // 高さ取得
    private bool GetHeight(float[,] heights)
    {
        // 高度レンダリング用カメラ高さを調整
        var position = _camera.transform.position;
        position.y = _heightRange[1] + 10f;
        _camera.transform.position = position;
        _camera.Render();

        var colors = GetPixels(_camera.targetTexture);
        if (colors == null)
        {
            Debug.LogError("Error");
            return false;
        }

        if (colors.Length != (_textureSize.x * _textureSize.y))
        {
            Debug.LogError("Error");
            return false;
        }


        var colorIndex = 0;
        for (int x = 0; x < _textureSize.x; ++x)
        {
            for (int y = 0; y < _textureSize.y; ++y)
            {
                var color = colors[colorIndex++];

                // Alpha値がゼロなら下限値固定。それ以外はRed値が高さとなる
                heights[x, y] = (color.a == 0 ? 0 : color.r);
            }
        }

        return true;
    }

    // 高さ更新
    private void UpdateHeight(MeshFilter meshFilter)
    {
        var mesh = meshFilter.sharedMesh;
        mesh.RecalculateBounds();

        var height = Height;
        var minHeight = _heightRange[0];

        var vertices = mesh.vertices;
        var colors = mesh.colors;
        for (int i = 0, count = mesh.vertexCount; i < count; ++i)
        {
            var v = transform.TransformPoint(vertices[i]);
            var color = colors[i];

            // 高さ範囲の下限をゼロ、上限を１として０〜１の範囲で高さを計算し
            // Red値にセットする
            color.r = Mathf.Clamp01((v.y - minHeight) / height);
            color.a = 1f;
            colors[i] = color;
        }
        mesh.SetColors(colors);
    }

    // 高度レンダリング結果取得
    private Color[] GetPixels(RenderTexture targetTexture)
    {
        Color[] colors = null;

        var currentRenderTexture = RenderTexture.active;
        {
            RenderTexture.active = targetTexture;

            // オフスクリーンレンダリング結果のピクセル列を取得
            var texture = new Texture2D(targetTexture.width, targetTexture.height);
            texture.ReadPixels(new Rect(0, 0, targetTexture.width, targetTexture.height), 0, 0);
            texture.Apply();

            colors = texture.GetPixels();
        }
        RenderTexture.active = currentRenderTexture;

        return colors;
    }
} // class GRTileMesh
