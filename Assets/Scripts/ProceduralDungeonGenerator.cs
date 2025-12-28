using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//
public class ProceduralDungeonGenerator : MonoBehaviour
{
    public enum EdgeType { Wall, Center, Left, Right, Open }

    [System.Serializable]
    public class TileConfig
    {
        public string tileName;
        public EdgeType north, east, south, west;

        public bool IsRoomTile()
        {
            return (north == EdgeType.Open || north == EdgeType.Wall) &&
                   (east == EdgeType.Open || east == EdgeType.Wall) &&
                   (south == EdgeType.Open || south == EdgeType.Wall) &&
                   (west == EdgeType.Open || west == EdgeType.Wall);
        }

        public bool IsFullyOpen()
        {
            return north == EdgeType.Open && east == EdgeType.Open &&
                   south == EdgeType.Open && west == EdgeType.Open;
        }

        public int GetOpeningCount()
        {
            int count = 0;
            if (north != EdgeType.Wall) count++;
            if (east != EdgeType.Wall) count++;
            if (south != EdgeType.Wall) count++;
            if (west != EdgeType.Wall) count++;
            return count;
        }
    }

    [Header("Tile Prefabs")]
    public GameObject[] allTilePrefabs;

    [Header("Generation Settings")]
    public int dungeonWidth = 12;
    public int dungeonHeight = 12;
    public float tileSize = 4f;
    public int targetTileCount = 50;

    [Header("Tile Preferences")]
    [Range(0f, 1f)] public float roomTileProbability = 0.65f;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public bool generateOnStart = false;

    private Dictionary<string, TileConfig> tileConfigs;
    private GameObject[,] placedTiles;
    private TileConfig[,] placedConfigs;

    void Start()
    {
        InitializeTileConfigs();
        if (generateOnStart) GenerateDungeon();
    }

    void InitializeTileConfigs()
    {
        tileConfigs = new Dictionary<string, TileConfig>();
        EdgeType W = EdgeType.Wall, C = EdgeType.Center, L = EdgeType.Left, R = EdgeType.Right, O = EdgeType.Open;

        // ══════════════════════════════════════════
        // CORRIDOR TILES (__ double underscore = + in docs)
        // Parameter order: Front, Right, Back, Left
        // ══════════════════════════════════════════

        // __Corner_01 series - center openings
        AddConfig("Tiles__Corner_01_A", C, W, W, C);  // F + L
        AddConfig("Tiles__Corner_01_B", C, C, W, W);  // F + R
        AddConfig("Tiles__Corner_01_C", W, W, C, C);  // L + B
        AddConfig("Tiles__Corner_01_D", W, C, C, W);  // R + B

        // __Corner_02 series - mixed center and open
        AddConfig("Tiles__Corner_02_A", C, O, O, C);  // F + L and R + B NO WALLS
        AddConfig("Tiles__Corner_02_B", C, C, O, O);  // F + R and L + B NO WALLS
        AddConfig("Tiles__Corner_02_C", O, O, C, C);  // L + B and R + F NO WALLS
        AddConfig("Tiles__Corner_02_D", O, C, C, O);  // R + B and L + F NO WALLS

        // __Cross
        AddConfig("Tiles__Cross", C, C, C, C);  // F + B + L + R

        // __Halls
        AddConfig("Tiles__Halls_01_A", C, W, C, W);  // F + B
        AddConfig("Tiles__Halls_01_B", W, C, W, C);  // L + R

        // __RoomEnd
        AddConfig("Tiles__RoomEnd_01_A", C, W, W, W);  // F
        AddConfig("Tiles__RoomEnd_01_B", W, W, W, C);  // L
        AddConfig("Tiles__RoomEnd_01_C", W, C, W, W);  // R
        AddConfig("Tiles__RoomEnd_01_D", W, W, C, W);  // B

        // __Room_Hole (same as RoomEnd)
        AddConfig("Tiles__Room_Hole_01_A", C, W, W, W);
        AddConfig("Tiles__Room_Hole_01_B", W, W, W, C);
        AddConfig("Tiles__Room_Hole_01_C", W, C, W, W);
        AddConfig("Tiles__Room_Hole_01_D", W, W, C, W);

        // __Room_HoleCeiling
        AddConfig("Tiles__Room_HoleCeiling_01_A", C, W, W, W);
        AddConfig("Tiles__Room_HoleCeiling_01_B", W, W, W, C);
        AddConfig("Tiles__Room_HoleCeiling_01_C", W, C, W, W);
        AddConfig("Tiles__Room_HoleCeiling_01_D", W, W, C, W);

        // __Room_HoleFloorCeiling
        AddConfig("Tiles__Room_HoleFloorCeiling_01_A", C, W, W, W);
        AddConfig("Tiles__Room_HoleFloorCeiling_01_B", W, W, W, C);
        AddConfig("Tiles__Room_HoleFloorCeiling_01_C", W, C, W, W);
        AddConfig("Tiles__Room_HoleFloorCeiling_01_D", W, W, C, W);

        // __Room_HoleFloor
        AddConfig("Tiles__Room_HoleFloor_01_A", C, W, W, W);
        AddConfig("Tiles__Room_HoleFloor_01_B", W, W, W, C);
        AddConfig("Tiles__Room_HoleFloor_01_C", W, C, W, W);
        AddConfig("Tiles__Room_HoleFloor_01_D", W, W, C, W);

        // __RoomStairs
        AddConfig("Tiles__RoomStairs_01_A", C, W, W, W);
        AddConfig("Tiles__RoomStairs_01_B", W, W, W, C);
        AddConfig("Tiles__RoomStairs_01_C", W, C, W, W);
        AddConfig("Tiles__RoomStairs_01_D", W, W, C, W);

        // __Side_01 - T-junctions
        AddConfig("Tiles__Side_01_A", C, C, W, C);  // F + L + R
        AddConfig("Tiles__Side_01_B", C, W, C, C);  // F + L + B
        AddConfig("Tiles__Side_01_C", C, C, C, W);  // F + R + B
        AddConfig("Tiles__Side_01_D", W, C, C, C);  // L + R + B

        // __Side_02 - Mixed
        AddConfig("Tiles__Side_02_A", C, C, O, C);  // F + L + R and B NO WALLS
        AddConfig("Tiles__Side_02_B", C, O, C, C);  // F + L + B and R NO WALLS
        AddConfig("Tiles__Side_02_C", C, C, C, O);  // F + R + B and L NO WALLS
        AddConfig("Tiles__Side_02_D", O, C, C, C);  // L + R + B and F NO WALLS

        // ══════════════════════════════════════════
        // ROOM TILES (BasicCorner/BasicSide)
        // ══════════════════════════════════════════

        // BasicCorner_01 - 2 adjacent open edges
        AddConfig("Tiles_BasicCorner_01_A", O, W, W, O);
        AddConfig("Tiles_BasicCorner_01_B", O, O, W, W);
        AddConfig("Tiles_BasicCorner_01_C", W, W, O, O);
        AddConfig("Tiles_BasicCorner_01_D", W, O, O, W);

        // BasicCorner_02
        AddConfig("Tiles_BasicCorner_02_A", O, W, C, O);
        AddConfig("Tiles_BasicCorner_02_B", O, O, C, W);
        AddConfig("Tiles_BasicCorner_02_C", C, W, O, O);
        AddConfig("Tiles_BasicCorner_02_D", C, O, O, W);

        // BasicCorner_03
        AddConfig("Tiles_BasicCorner_03_A", O, C, W, O);
        AddConfig("Tiles_BasicCorner_03_B", O, O, W, C);
        AddConfig("Tiles_BasicCorner_03_C", W, C, O, O);
        AddConfig("Tiles_BasicCorner_03_D", W, O, O, C);

        // BasicCorner_04
        AddConfig("Tiles_BasicCorner_04_A", O, W, L, O);
        AddConfig("Tiles_BasicCorner_04_B", O, O, R, W);
        AddConfig("Tiles_BasicCorner_04_C", R, W, O, O);
        AddConfig("Tiles_BasicCorner_04_D", L, O, O, W);

        // BasicCorner_05
        AddConfig("Tiles_BasicCorner_05_A", O, R, W, O);
        AddConfig("Tiles_BasicCorner_05_B", O, O, W, L);
        AddConfig("Tiles_BasicCorner_05_C", W, L, O, O);
        AddConfig("Tiles_BasicCorner_05_D", W, O, O, R);

        // BasicCorner_06 - TRANSITION tiles
        AddConfig("Tiles_BasicCorner_06_A", O, C, C, O);
        AddConfig("Tiles_BasicCorner_06_B", O, O, C, C);
        AddConfig("Tiles_BasicCorner_06_C", C, C, O, O);
        AddConfig("Tiles_BasicCorner_06_D", C, O, O, C);

        // BasicSide_01 - 3 open edges
        AddConfig("Tiles_BasicSide_01_A", O, O, W, O);
        AddConfig("Tiles_BasicSide_01_B", O, W, O, O);
        AddConfig("Tiles_BasicSide_01_C", O, O, O, W);
        AddConfig("Tiles_BasicSide_01_D", W, O, O, O);

        // BasicSide_02 - 1 center + 3 open
        AddConfig("Tiles_BasicSide_02_A", O, O, C, O);
        AddConfig("Tiles_BasicSide_02_B", O, C, O, O);
        AddConfig("Tiles_BasicSide_02_C", O, O, O, C);
        AddConfig("Tiles_BasicSide_02_D", C, O, O, O);

        // ══════════════════════════════════════════
        // T SERIES
        // ══════════════════════════════════════════

        AddConfig("Tiles_T__01_A", L, C, R, C);
        AddConfig("Tiles_T__01_B", R, C, L, C);
        AddConfig("Tiles_T__02_A", C, R, C, L);
        AddConfig("Tiles_T__02_B", C, L, C, R);
        AddConfig("Tiles_T__03_A", L, C, R, C);
        AddConfig("Tiles_T__03_B", R, C, L, C);
        AddConfig("Tiles_T__04_A", C, R, C, L);
        AddConfig("Tiles_T__04_B", C, L, C, R);

        AddConfig("Tiles_T_Side_01_A", L, W, W, C);
        AddConfig("Tiles_T_Side_01_B", R, C, W, W);
        AddConfig("Tiles_T_Side_01_C", W, W, R, C);
        AddConfig("Tiles_T_Side_01_D", W, C, L, W);

        AddConfig("Tiles_T_Side_02_A", W, W, C, L);
        AddConfig("Tiles_T_Side_02_B", C, W, W, R);
        AddConfig("Tiles_T_Side_02_C", W, R, C, W);
        AddConfig("Tiles_T_Side_02_D", C, L, W, W);

        AddConfig("Tiles_T_Side_03_A", L, W, O, C);
        AddConfig("Tiles_T_Side_03_B", R, C, O, W);
        AddConfig("Tiles_T_Side_03_C", O, W, R, C);
        AddConfig("Tiles_T_Side_03_D", O, C, L, W);

        AddConfig("Tiles_T_Side_04_A", O, O, W, L);
        AddConfig("Tiles_T_Side_04_B", W, O, O, R);
        AddConfig("Tiles_T_Side_04_C", O, O, W, L);
        AddConfig("Tiles_T_Side_04_D", W, L, O, O);

        AddConfig("Tiles_TCorner_01_A", L, W, W, W);
        AddConfig("Tiles_TCorner_01_B", R, W, W, W);
        AddConfig("Tiles_TCorner_01_C", W, W, R, W);
        AddConfig("Tiles_TCorner_01_D", W, W, L, W);

        AddConfig("Tiles_TCorner_02_A", W, W, W, L);
        AddConfig("Tiles_TCorner_02_B", W, W, W, R);
        AddConfig("Tiles_TCorner_02_C", W, R, W, W);
        AddConfig("Tiles_TCorner_02_D", W, L, W, W);

        AddConfig("Tiles_TCorner_03_A", L, O, O, W);
        AddConfig("Tiles_TCorner_03_B", R, W, O, O);
        AddConfig("Tiles_TCorner_03_C", O, O, R, W);
        AddConfig("Tiles_TCorner_03_D", O, W, L, O);

        AddConfig("Tiles_TCorner_04_A", O, O, W, L);
        AddConfig("Tiles_TCorner_04_B", W, O, O, R);
        AddConfig("Tiles_TCorner_04_C", O, R, W, O);
        AddConfig("Tiles_TCorner_04_D", W, L, O, O);

        AddConfig("Tiles_TCornerSide_01_A", L, W, R, C);
        AddConfig("Tiles_TCornerSide_01_B", R, C, L, W);
        AddConfig("Tiles_TCornerSide_02_A", W, R, C, L);
        AddConfig("Tiles_TCornerSide_02_B", C, L, W, R);
        AddConfig("Tiles_TCornerSide_03_A", L, O, R, C);
        AddConfig("Tiles_TCornerSide_03_B", R, C, L, O);
        AddConfig("Tiles_TCornerSide_04_A", O, R, C, L);
        AddConfig("Tiles_TCornerSide_04_B", C, L, O, R);

        AddConfig("Tiles_THalls_01_A", R, W, L, W);
        AddConfig("Tiles_THalls_01_B", L, W, R, W);
        AddConfig("Tiles_THalls_01_C", W, R, W, L);
        AddConfig("Tiles_THalls_01_D", W, L, W, R);

        AddConfig("Tiles_TStairs_01_A", R, W, W, W);
        AddConfig("Tiles_TStairs_01_B", W, W, W, R);
        AddConfig("Tiles_TStairs_01_C", W, R, W, W);
        AddConfig("Tiles_TStairs_01_D", W, W, L, W);

        AddConfig("Tiles_TStairs_02_A", L, W, W, W);
        AddConfig("Tiles_TStairs_02_B", W, W, W, L);
        AddConfig("Tiles_TStairs_02_C", W, L, W, W);
        AddConfig("Tiles_TStairs_02_D", W, W, L, W);

        // Fill tile - all walls
        AddConfig("Tiles_01_Fill", W, W, W, W);

        Debug.Log($"Initialized {tileConfigs.Count} tile configurations");
    }
    void AddConfig(string name, EdgeType north, EdgeType east, EdgeType south, EdgeType west)
    {
        tileConfigs[name] = new TileConfig { tileName = name, north = north, east = east, south = south, west = west };
    }

    [ContextMenu("Generate Dungeon")]
    public void GenerateDungeon()
    {
        if (tileConfigs == null || tileConfigs.Count == 0) InitializeTileConfigs();
        if (allTilePrefabs == null || allTilePrefabs.Length == 0)
        {
            Debug.LogError("No tile prefabs assigned!");
            return;
        }

        ClearDungeon();

        // Create parent for this level
        GameObject levelParent = new GameObject("Level_0");
        levelParent.transform.parent = transform;

        // Reset arrays
        placedTiles = new GameObject[dungeonWidth, dungeonHeight];
        placedConfigs = new TileConfig[dungeonWidth, dungeonHeight];

        // Start from center
        int startX = dungeonWidth / 2;
        int startZ = dungeonHeight / 2;

        PlaceRandomRoomTile(startX, startZ, levelParent);

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        frontier.Enqueue(new Vector2Int(startX, startZ));
        visited.Add(new Vector2Int(startX, startZ));
        int tilesPlaced = 1;

        int attempts = 0, maxAttempts = targetTileCount * 15;

        while (frontier.Count > 0 && tilesPlaced < targetTileCount && attempts < maxAttempts)
        {
            attempts++;
            Vector2Int current = frontier.Dequeue();
            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y), new Vector2Int(current.x + 1, current.y),
                new Vector2Int(current.x, current.y + 1), new Vector2Int(current.x, current.y - 1)
            };

            // Shuffle neighbors for variety
            for (int i = 0; i < neighbors.Length; i++)
            {
                int randIndex = Random.Range(i, neighbors.Length);
                Vector2Int temp = neighbors[i]; neighbors[i] = neighbors[randIndex]; neighbors[randIndex] = temp;
            }

            foreach (Vector2Int neighbor in neighbors)
            {
                if (!IsInBounds(neighbor.x, neighbor.y) || visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                if (TryPlaceCompatibleTile(neighbor.x, neighbor.y, levelParent))
                {
                    frontier.Enqueue(neighbor);
                    tilesPlaced++;
                    if (tilesPlaced >= targetTileCount) break;
                }
            }
        }

        Debug.Log($"Generated dungeon: {tilesPlaced} tiles in {attempts} attempts");
    }

    bool IsInBounds(int x, int z) { return x >= 0 && x < dungeonWidth && z >= 0 && z < dungeonHeight; }

    void PlaceRandomRoomTile(int x, int z, GameObject parent)
    {
        List<GameObject> roomTiles = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab != null && tileConfigs.ContainsKey(prefab.name) && !prefab.name.Contains("Stairs"))
            {
                TileConfig config = tileConfigs[prefab.name];
                if (config.IsRoomTile())
                {
                    bool validForPosition = true;
                    if (x == 0 && config.west != EdgeType.Wall) validForPosition = false;
                    if (x == dungeonWidth - 1 && config.east != EdgeType.Wall) validForPosition = false;
                    if (z == 0 && config.south != EdgeType.Wall) validForPosition = false;
                    if (z == dungeonHeight - 1 && config.north != EdgeType.Wall) validForPosition = false;

                    if (validForPosition) roomTiles.Add(prefab);
                }
            }
        }

        if (roomTiles.Count > 0)
        {
            PlaceTile(x, z, roomTiles[Random.Range(0, roomTiles.Count)], parent);
        }
        else
        {
            TryPlaceCompatibleTile(x, z, parent);
        }
    }

    bool TryPlaceCompatibleTile(int x, int z, GameObject parent)
    {
        // Try strict matching only
        List<GameObject> compatibleTiles = new List<GameObject>();
        List<GameObject> roomTiles = new List<GameObject>();

        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            TileConfig config = tileConfigs[prefab.name];
            if (IsCompatibleWithNeighbors(x, z, config))
            {
                if (config.IsRoomTile()) roomTiles.Add(prefab);
                else compatibleTiles.Add(prefab);
            }
        }

        GameObject chosen = null;

        if (roomTiles.Count > 0 && Random.value < roomTileProbability)
            chosen = roomTiles[Random.Range(0, roomTiles.Count)];
        else if (compatibleTiles.Count > 0)
            chosen = compatibleTiles[Random.Range(0, compatibleTiles.Count)];
        else if (roomTiles.Count > 0)
            chosen = roomTiles[Random.Range(0, roomTiles.Count)];

        if (chosen != null)
        {
            PlaceTile(x, z, chosen, parent);
            return true;
        }

        // Fallback: use Fill tile
        GameObject fillTile = System.Array.Find(allTilePrefabs, p => p != null && p.name == "Tiles_01_Fill");
        if (fillTile != null)
        {
            PlaceTile(x, z, fillTile, parent);
            Debug.LogWarning($"Used fill tile at ({x},{z}) - dead end");
            return false;
        }

        return false;
    }

    bool IsCompatibleWithNeighbors(int x, int z, TileConfig config)
    {
        // Perimeter validation
        if (x == 0 && config.west != EdgeType.Wall) return false;
        if (x == dungeonWidth - 1 && config.east != EdgeType.Wall) return false;
        if (z == 0 && config.south != EdgeType.Wall) return false;
        if (z == dungeonHeight - 1 && config.north != EdgeType.Wall) return false;

        // Check compatibility with placed neighbors (strict matching)
        if (x > 0 && placedConfigs[x - 1, z] != null && !EdgesMatch(config.west, placedConfigs[x - 1, z].east)) return false;
        if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && !EdgesMatch(config.east, placedConfigs[x + 1, z].west)) return false;
        if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && !EdgesMatch(config.north, placedConfigs[x, z + 1].south)) return false;
        if (z > 0 && placedConfigs[x, z - 1] != null && !EdgesMatch(config.south, placedConfigs[x, z - 1].north)) return false;
        return true;
    }

    bool EdgesMatch(EdgeType mine, EdgeType theirs)
    {
        // Wall to Wall
        if (mine == EdgeType.Wall && theirs == EdgeType.Wall) return true;

        // Wall to anything else
        if (mine == EdgeType.Wall || theirs == EdgeType.Wall) return false;

        // Open ONLY matches Open
        if (mine == EdgeType.Open || theirs == EdgeType.Open)
            return mine == EdgeType.Open && theirs == EdgeType.Open;

        // Center to Center
        if (mine == EdgeType.Center && theirs == EdgeType.Center) return true;

        // Left to Right (mirrored)
        if (mine == EdgeType.Left && theirs == EdgeType.Right) return true;
        if (mine == EdgeType.Right && theirs == EdgeType.Left) return true;

        return false;
    }

    void PlaceTile(int x, int z, GameObject prefab, GameObject parent)
    {
        Vector3 pos = new Vector3(x * tileSize, parent.transform.position.y, z * tileSize);
        placedTiles[x, z] = Instantiate(prefab, pos, Quaternion.identity, parent.transform);
        placedTiles[x, z].name = $"Tile_{x}_{z}_{prefab.name}";
        if (tileConfigs.ContainsKey(prefab.name)) placedConfigs[x, z] = tileConfigs[prefab.name];
    }

    public void ClearDungeon()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
        placedTiles = null;
        placedConfigs = null;
    }

    // Public getters for editor and other systems
    public int GetWidth() { return dungeonWidth; }
    public int GetHeight() { return dungeonHeight; }
    public float GetTileSize() { return tileSize; }
    public TileConfig[,] GetPlacedConfigs() { return placedConfigs; }
    public GameObject[,] GetPlacedTiles() { return placedTiles; }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || placedConfigs == null) return;

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedConfigs[x, z] == null) continue;
                Vector3 pos = new Vector3(x * tileSize, 0.1f, z * tileSize);

                if (placedConfigs[x, z].tileName == "Tiles_01_Fill")
                    Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                else if (placedConfigs[x, z].IsRoomTile())
                    Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
                else
                    Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);

                Gizmos.DrawCube(pos, new Vector3(tileSize * 0.9f, 0.1f, tileSize * 0.9f));
            }
        }
    }
}
