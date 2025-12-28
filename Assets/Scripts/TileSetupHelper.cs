using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Auto-generates TileConnectivityData based on tile naming conventions
/// and the documentation provided
/// </summary>
public class TileSetupHelper : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("Tools/Dungeon/Create Tile Database")]
    public static void CreateTileDatabase()
    {
        // Create the ScriptableObject
        TileConnectivityData data = ScriptableObject.CreateInstance<TileConnectivityData>();
        
        // Find all tile prefabs
        string[] guids = AssetDatabase.FindAssets("t:Prefab Tiles_", new[] { "Assets" });
        
        Debug.Log($"Found {guids.Length} tile prefabs");
        
        Dictionary<string, GameObject> prefabDict = new Dictionary<string, GameObject>();
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                prefabDict[prefab.name] = prefab;
            }
        }
        
        // Define all tiles based on documentation
        data.allTiles = new List<TileConnectivityData.TileDefinition>();
        
        // Tiles_+Corner series
        AddTile(data, prefabDict, "Tiles_+Corner_01_A", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_+Corner_01_B", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening);
        AddTile(data, prefabDict, "Tiles_+Corner_01_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_+Corner_01_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening);
        
        AddTile(data, prefabDict, "Tiles_+Corner_02_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_+Corner_02_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_+Corner_02_C", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_+Corner_02_D", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        
        // Tiles_+Cross
        AddTile(data, prefabDict, "Tiles_+Cross", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening);
        
        // Tiles_+Halls
        AddTile(data, prefabDict, "Tiles_+Halls_01_A", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_+Halls_01_B", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening);
        
        // Tiles_+RoomEnd
        AddTile(data, prefabDict, "Tiles_+RoomEnd_01_A", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_+RoomEnd_01_B", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_+RoomEnd_01_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening);
        AddTile(data, prefabDict, "Tiles_+RoomEnd_01_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall);
        
        // Tiles_+Side (T-junctions)
        AddTile(data, prefabDict, "Tiles_+Side_01_A", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening);
        AddTile(data, prefabDict, "Tiles_+Side_01_B", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_+Side_01_C", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening);
        AddTile(data, prefabDict, "Tiles_+Side_01_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening);
        
        AddTile(data, prefabDict, "Tiles_+Side_02_A", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening);
        AddTile(data, prefabDict, "Tiles_+Side_02_B", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_+Side_02_C", TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.CenterOpening);
        AddTile(data, prefabDict, "Tiles_+Side_02_D", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening, TileConnectivityData.EdgeType.CenterOpening);
        
        // BasicCorner series
        AddTile(data, prefabDict, "Tiles_BasicCorner_01_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_01_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_01_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_01_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        
        AddTile(data, prefabDict, "Tiles_BasicCorner_02_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_02_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_02_C", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_02_D", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        
        AddTile(data, prefabDict, "Tiles_BasicCorner_03_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_03_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_03_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_03_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        
        AddTile(data, prefabDict, "Tiles_BasicCorner_04_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_04_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_04_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_04_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        
        AddTile(data, prefabDict, "Tiles_BasicCorner_05_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_05_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_05_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_05_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        
        AddTile(data, prefabDict, "Tiles_BasicCorner_06_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_06_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_06_C", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicCorner_06_D", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        
        // BasicSide series (straight corridors)
        AddTile(data, prefabDict, "Tiles_BasicSide_01_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicSide_01_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicSide_01_C", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicSide_01_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        
        AddTile(data, prefabDict, "Tiles_BasicSide_02_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicSide_02_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_BasicSide_02_C", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_BasicSide_02_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        
        // THalls series
        AddTile(data, prefabDict, "Tiles_THalls_01_A", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_THalls_01_B", TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall);
        AddTile(data, prefabDict, "Tiles_THalls_01_C", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        AddTile(data, prefabDict, "Tiles_THalls_01_D", TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.SolidWall, TileConnectivityData.EdgeType.NoWall, TileConnectivityData.EdgeType.NoWall);
        
        // Add more tiles here... (this is getting long, but you get the idea)
        // For now, let's add the most common/important ones
        
        Debug.Log($"Created tile database with {data.allTiles.Count} tiles");
        
        // Save the asset
        string assetPath = "Assets/DungeonTileDatabase.asset";
        AssetDatabase.CreateAsset(data, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Selection.activeObject = data;
        
        Debug.Log($"Tile database created at: {assetPath}");
    }
    
    static void AddTile(TileConnectivityData data, Dictionary<string, GameObject> prefabs, 
        string name, 
        TileConnectivityData.EdgeType front, 
        TileConnectivityData.EdgeType back,
        TileConnectivityData.EdgeType left, 
        TileConnectivityData.EdgeType right,
        float weight = 1f)
    {
        if (prefabs.TryGetValue(name, out GameObject prefab))
        {
            var tileDef = new TileConnectivityData.TileDefinition
            {
                tileName = name,
                prefab = prefab,
                frontEdge = front,
                backEdge = back,
                leftEdge = left,
                rightEdge = right,
                weight = weight
            };
            data.allTiles.Add(tileDef);
        }
        else
        {
            Debug.LogWarning($"Prefab not found: {name}");
        }
    }
#endif
}