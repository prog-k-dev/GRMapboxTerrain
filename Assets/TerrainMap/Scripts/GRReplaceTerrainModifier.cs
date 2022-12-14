using System;
using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Unity.MeshGeneration.Data;
using Mapbox.Unity.MeshGeneration.Interfaces;
using Mapbox.Unity.MeshGeneration.Modifiers;
using Mapbox.Unity.Utilities;
using Mapbox.Utils;
using UnityEngine;


[CreateAssetMenu(menuName = "Grenge/Modifiers/Replace Terrain Modifier")]
public class GRReplaceTerrainModifier : GameObjectModifier
{
    [SerializeField]
    private GameObject _tileMeshPrefab = null;

    private Dictionary<string, GRTileMesh> _tileMeshes = new Dictionary<string, GRTileMesh>();


    public override void Run(VectorEntity ve, UnityTile tile)
    {
        // NOTE: タイル内に建物が一つもない場合は、ここも一度も呼ばれない
        var tileMesh = GetTileMesh(tile);
        if (tileMesh == null)
        {
            return;
        }

        if (!tileMesh.AddMesh(ve))
        {
            return;
        }
    }

    private GRTileMesh GetTileMesh(UnityTile tile)
    {
        if (_tileMeshes.TryGetValue(tile.name, out var tileMesh))
        {
            return tileMesh;
        }

        if (!CreateTileMesh(tile, out tileMesh))
        {
            return null;
        }
        _tileMeshes.Add(tile.name, tileMesh);

        return tileMesh;

    }

    private bool CreateTileMesh(UnityTile tile, out GRTileMesh tileMesh)
    {
        tileMesh = null;

        if (_tileMeshPrefab == null)
        {
            Debug.LogError("Error");
            return false;
        }

        GameObject obj = GameObject.Instantiate(_tileMeshPrefab);
        obj.transform.SetParent(tile.transform);
        obj.transform.localPosition = Vector3.zero;

        tileMesh = obj.GetComponent<GRTileMesh>();
        if (tileMesh == null)
        {
            Debug.LogError("Error");
            return false;
        }
        if (!tileMesh.SetTile(tile))
        {
            return false;
        }

        return true;
    }

    public override void Clear()
    {
        _tileMeshes.Clear();
    }
} // class GRReplaceTerrainModifier
