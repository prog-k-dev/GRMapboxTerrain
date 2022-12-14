using System.Collections;
using System.Collections.Generic;
using com.heparo.terrain.toolkit;
using Mapbox.Map;
using Mapbox.Unity.Map;
using Mapbox.Unity.MeshGeneration.Data;
using UnityEngine;


public class GRCustomMap : MonoBehaviour
{
    // Mapbox地図
    [SerializeField]
    private AbstractMap _abstractMap = null;

    // Terrainプレハブ
    [SerializeField]
    private GameObject _terrainPrefab = null;

    // 描画中タイル
    private Dictionary<string, GRTileMesh> _tileMeshes = new Dictionary<string, GRTileMesh>();
    // 描画中Terrain
    private Dictionary<string, Terrain> _terrains = new Dictionary<string, Terrain>();

    // メッシュ表示フラグ（ON：メッシュ表示、OFF：Terrain表示）
    private bool _showMesh = true;
    // TerrainToolkitコンポーネント（Terrain加工用）
    private TerrainToolkit _terrainToolkit;

    public void OnChangeMesh(UnityEngine.UI.Text text)
    {
        _showMesh = !_showMesh;
        text.text = (_showMesh ? "Terrain表示" : "Mesh表示");

        foreach (var tileName in _terrains.Keys)
        {
            UpdateMesh(tileName);
        }
    }

    private void Start()
    {
        if (_abstractMap != null)
        {
            // イベントハンドラ設定
            _abstractMap.OnTileFinished += OnTileFinished;
            _abstractMap.OnTilesStarting += OnTilesStarting;
            _abstractMap.OnTilesDisposing += OnTilesDisposing;
        }
    }

    // タイル処理完了
    private void OnTileFinished(UnityTile tile)
    {
        Debug.LogFormat("OnTileFinished({0})", tile.name);

        if (!AddTile(tile))
        {
            return;
        }
    }

    // タイル処理開始
    private void OnTilesStarting(List<UnwrappedTileId> tileIds)
    {
        Debug.LogFormat("OnTilesStarting()：{0} Tiles", tileIds.Count);
        foreach (var tileId in tileIds)
        {
            Debug.LogFormat("  tile：{0}", tileId.ToString());
        }
    }

    // タイル破棄
    private void OnTilesDisposing(List<UnwrappedTileId> tileIds)
    {
        Debug.LogFormat("OnTilesDisposing()：{0} Tiles", tileIds.Count);
        foreach (var tileId in tileIds)
        {
            var key = tileId.ToString();
            Debug.LogFormat("  tile：{0}", key);

            // Terrain破棄
            if (_terrains.TryGetValue(key, out var terrain))
            {
                GameObject.DestroyImmediate(terrain.transform.parent.gameObject);
                _terrains.Remove(key);
            }

            // タイルデータ破棄
            if (_tileMeshes.TryGetValue(key, out var tileMesh))
            {
                GameObject.DestroyImmediate(tileMesh.gameObject);
                _tileMeshes.Remove(key);
            }
        }
    }

    // タイル追加
    private bool AddTile(UnityTile tile)
    {
        if (_terrainPrefab == null)
        {
            Debug.LogError("Error");
            return false;
        }

        var tileName = tile.name;
        if (_terrains.ContainsKey(tileName))
        {
            Debug.LogError("Error");
            return false;
        }

        GRTileMesh tileMesh = tile.gameObject.GetComponentInChildren<GRTileMesh>();
        if (tileMesh == null)
        {
            // NOTE: 建物が一つもない場所の場合は、この時点でGRTileMeshも生成されない
            // NOTE: その場合は、ここでもGRReplaceTerrainModifierと同様にGRTileMeshを生成してやる必要がある
            return false;
        }

        // Terrainインスタンス化
        var terrainRoot = GameObject.Instantiate(_terrainPrefab);
        terrainRoot.transform.SetParent(tile.transform);

        var terrain = terrainRoot.transform.GetComponentInChildren<Terrain>();
        if (terrain == null)
        {
            Debug.LogError("Error");
            return false;
        }
        _terrains.Add(tileName, terrain);

        _terrainToolkit = terrain.GetComponent<TerrainToolkit>();
        if (_terrainToolkit == null)
        {
            Debug.LogError("Error");
            return false;
        }

        // Terrainデータ複製
        var terrainData =　TerrainData.Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        var collider = terrain.GetComponent<TerrainCollider>();
        if (collider != null)
        {
            collider.terrainData = terrainData;
        }

        // Terrain解像度設定
        var resolution = tileMesh.Resolution;
        terrainData.alphamapResolution = resolution;
        terrainData.heightmapResolution = resolution + 1;

        // Terrainサイズ設定
        var bounds = tile.MeshFilter.sharedMesh.bounds;
        var terrainSize = bounds.size;
        terrainSize.y = tileMesh.Height;
        terrainData.size = terrainSize;

        // Terrain位置調整
        var position = new Vector3(-bounds.extents.x, 0, -bounds.extents.z);
        terrainRoot.transform.localPosition = position;
        position = terrainRoot.transform.position;
        position.y = tileMesh.HeightRange[0];
        terrainRoot.transform.position = position;

        // Terrainアルファマップ、ハイトマップ更新
        float[,] heights = null;
        float[,,] alphaMaps = null;
        {
            // 地面高さ取得
            heights = tileMesh.GetGroundHeight();
            if (heights == null)
            {
                return false;
            }

            // 建物高さ取得
            var featureHeights = tileMesh.GetFeatureHeight();
            if (featureHeights == null)
            {
                return false;
            }

            if (heights.Length != featureHeights.Length)
            {
                Debug.LogError("Error");
                return false;
            }

            alphaMaps = terrainData.GetAlphamaps(0, 0, resolution, resolution);
            for (int x = 0, countX = heights.GetLength(0); x < countX; ++x)
            {
                for (int y = 0, countY = heights.GetLength(1); y < countY; ++y)
                {
                    var groundHeight = heights[x, y];
                    var featureHeight = featureHeights[x, y];
                    if (featureHeight <= groundHeight)
                    {
                        // 地面部分アルファマップ設定
                        alphaMaps[x, y, 0] = 1;
                        alphaMaps[x, y, 1] = 0;
                    }
                    else
                    {
                        // 建物部分アルファマップ、高さ設定
                        heights[x, y] = featureHeight;
                        alphaMaps[x, y, 0] = 0;
                        alphaMaps[x, y, 1] = 1;
                    }
                }
            }
        }
        terrainData.SetAlphamaps(0, 0, alphaMaps);
        terrainData.SetHeights(0, 0, heights);

        // TerrainToolkitを利用してTerrainをスムージング
        _terrainToolkit.SmoothTerrain(1, 0.414f);

        terrain.gameObject.SetActive(false);
        _tileMeshes.Add(tileName, tileMesh);
        UpdateMesh(tileName);

        return true;
    }

    // メッシュ更新
    private void UpdateMesh(string tileName)
    {
        // メッシュ表示・非表示
        if (_tileMeshes.TryGetValue(tileName, out var tileMesh))
        {
            tileMesh.ShowMesh(_showMesh);
        }

        // Terrain表示・非表示
        if (_terrains.TryGetValue(tileName, out var terrain))
        {
            terrain.gameObject.SetActive(!_showMesh);
        }
    }

    private void OnDestroy()
    {
        if (_abstractMap != null)
        {
            // イベントハンドラ解除
            _abstractMap.OnTileFinished -= OnTileFinished;
            _abstractMap.OnTilesStarting -= OnTilesStarting;
            _abstractMap.OnTilesDisposing -= OnTilesDisposing;
        }
    }
} // class GRCustomMap
