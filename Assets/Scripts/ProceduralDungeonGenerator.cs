using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

    [Header("Decoration Prefabs")]
    public GameObject wallPrefab;
    public GameObject wall2mPrefab;
    public GameObject wallDoorPrefab;
    public GameObject doorSnapperPrefab;
    public GameObject pillerPrefab;

    [Header("Generation Settings")]
    public int dungeonWidth = 12;
    public int dungeonHeight = 12;
    public float tileSize = 4f;
    public int targetTileCount = 50;

    [Header("Tile Preferences")]
    [Range(0f, 1f)] public float roomTileProbability = 0.65f;

    [Header("Decoration Settings")]
    [Range(0f, 1f)] public float pillarChance = 0.2f;
    [Range(0f, 1f)] public float doorChance = 0.25f;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public bool generateOnStart = false;

    [Header("Multi-Level Settings")]
    public int numberOfLevels = 2;
    public float levelHeight = 4f; // Distance between levels

    private List<GameObject> levelParents = new List<GameObject>();

    // Used by the deferred NavMesh coroutine — collects per-level data during tile placement
    // so the bake can happen one frame later, after Unity registers all MeshRenderers.
    private bool deferNavMesh = false;
    private List<(GameObject parent, int index, TileConfig[,] configs)> pendingNavMeshLevels
        = new List<(GameObject, int, TileConfig[,])>();

    private Dictionary<string, TileConfig> tileConfigs;
    private GameObject[,] placedTiles;
    private TileConfig[,] placedConfigs;
    private List<GameObject> decorationObjects;
    private bool isRepairing = false; // Relaxes some placement rules during repair passes
    private List<Vector2Int> stairsPositions = new List<Vector2Int>(); // Track where stairs were placed
    private Dictionary<int, TileConfig[,]> allLevelConfigs = new Dictionary<int, TileConfig[,]>();

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
        AddConfig("Tiles_BasicCorner_01_A", O, W, W, O);  // F + L NO WALLS
        AddConfig("Tiles_BasicCorner_01_B", O, O, W, W);  // F + R NO WALLS
        AddConfig("Tiles_BasicCorner_01_C", W, W, O, O);  // L + B NO WALLS
        AddConfig("Tiles_BasicCorner_01_D", W, O, O, W);  // R + B NO WALLS

        // BasicCorner_02 - 1 center + 2 open
        AddConfig("Tiles_BasicCorner_02_A", O, W, C, O);  // B and F + L NO WALLS
        AddConfig("Tiles_BasicCorner_02_B", O, O, C, W);  // B and F + R NO WALLS
        AddConfig("Tiles_BasicCorner_02_C", C, W, O, O);  // F and L + B NO WALLS
        AddConfig("Tiles_BasicCorner_02_D", C, O, O, W);  // F and R + B NO WALLS

        // BasicCorner_03 - 1 center + 2 open
        AddConfig("Tiles_BasicCorner_03_A", O, C, W, O);  // R and F + L NO WALLS
        AddConfig("Tiles_BasicCorner_03_B", O, O, W, C);  // L and F + R NO WALLS
        AddConfig("Tiles_BasicCorner_03_C", W, C, O, O);  // R and L + B NO WALLS
        AddConfig("Tiles_BasicCorner_03_D", W, O, O, C);  // L and R + B NO WALLS

        // BasicCorner_04 - 1 left/right + 2 open
        AddConfig("Tiles_BasicCorner_04_A", O, W, L, O);  // B right → B left (flipped for back)
        AddConfig("Tiles_BasicCorner_04_B", O, O, R, W);  // B left → B right (flipped for back)
        AddConfig("Tiles_BasicCorner_04_C", R, W, O, O);  // F right and L + B NO WALLS
        AddConfig("Tiles_BasicCorner_04_D", L, O, O, W);  // F left and R + B NO WALLS

        // BasicCorner_05 - 1 left/right + 2 open
        AddConfig("Tiles_BasicCorner_05_A", O, R, W, O);  // R right and L + F NO WALLS
        AddConfig("Tiles_BasicCorner_05_B", O, O, W, L);  // L left and F + R NO WALLS
        AddConfig("Tiles_BasicCorner_05_C", W, L, O, O);  // R left and L + B NO WALLS
        AddConfig("Tiles_BasicCorner_05_D", W, O, O, R);  // L right and R + B NO WALLS

        // BasicCorner_06 - TRANSITION tiles (2 center + 2 open)
        AddConfig("Tiles_BasicCorner_06_A", O, C, C, O);  // R + B and L + F NO WALLS
        AddConfig("Tiles_BasicCorner_06_B", O, O, C, C);  // L + B and F + R NO WALLS
        AddConfig("Tiles_BasicCorner_06_C", C, C, O, O);  // F + R and L + B NO WALLS
        AddConfig("Tiles_BasicCorner_06_D", C, O, O, C);  // Assuming pattern

        // BasicSide_01 - 3 open edges
        AddConfig("Tiles_BasicSide_01_A", O, O, W, O);  // L + F + R NO WALLS
        AddConfig("Tiles_BasicSide_01_B", O, W, O, O);  // L + F + B NO WALLS
        AddConfig("Tiles_BasicSide_01_C", O, O, O, W);  // F + B + R NO WALLS
        AddConfig("Tiles_BasicSide_01_D", W, O, O, O);  // L + B + R NO WALLS

        // BasicSide_02 - 1 center + 3 open
        AddConfig("Tiles_BasicSide_02_A", O, O, C, O);  // B and L + F + R NO WALLS
        AddConfig("Tiles_BasicSide_02_B", O, C, O, O);  // R and L + F + B NO WALLS
        AddConfig("Tiles_BasicSide_02_C", O, O, O, C);  // L and B + F + R NO WALLS
        AddConfig("Tiles_BasicSide_02_D", C, O, O, O);  // F and L + B + R NO WALLS

        // ══════════════════════════════════════════
        // T SERIES (left/right offset openings)
        // ══════════════════════════════════════════

        // T__ - 4-way with offsets
        AddConfig("Tiles_T__01_A", L, C, R, C);  // F left + L + R + B left (flipped to B right)
        AddConfig("Tiles_T__01_B", R, C, L, C);  // F right + L + R + B right (flipped to B left)
        AddConfig("Tiles_T__02_A", C, R, C, L);  // F + L left + R right + B
        AddConfig("Tiles_T__02_B", C, L, C, R);  // F + L right + R left + B
        AddConfig("Tiles_T__03_A", L, C, R, C);  // Same as 01_A (flipped)
        AddConfig("Tiles_T__03_B", R, C, L, C);  // Same as 01_B (flipped)
        AddConfig("Tiles_T__04_A", C, R, C, L);  // Same as 02_A
        AddConfig("Tiles_T__04_B", C, L, C, R);  // Same as 02_B

        // T_Side - 2-way with offsets
        AddConfig("Tiles_T_Side_01_A", L, W, W, C);  // F left + L
        AddConfig("Tiles_T_Side_01_B", R, C, W, W);  // F right + R
        AddConfig("Tiles_T_Side_01_C", W, W, R, C);  // B left (flipped to B right) + L
        AddConfig("Tiles_T_Side_01_D", W, C, L, W);  // B right (flipped to B left) + R

        AddConfig("Tiles_T_Side_02_A", W, W, C, L);  // L left + B
        AddConfig("Tiles_T_Side_02_B", C, W, W, R);  // L right + F
        AddConfig("Tiles_T_Side_02_C", W, R, C, W);  // R right + B
        AddConfig("Tiles_T_Side_02_D", C, L, W, W);  // F + R left

        AddConfig("Tiles_T_Side_03_A", L, W, O, C);  // F left + L + B NO WALLS
        AddConfig("Tiles_T_Side_03_B", R, C, O, W);  // F right + R + B NO WALLS
        AddConfig("Tiles_T_Side_03_C", O, W, R, C);  // B left (flipped to B right) + L + F NO WALLS
        AddConfig("Tiles_T_Side_03_D", O, C, L, W);  // B right (flipped to B left) + R + F NO WALLS

        AddConfig("Tiles_T_Side_04_A", O, O, W, L);  // L left and F + R NO WALLS
        AddConfig("Tiles_T_Side_04_B", W, O, O, R);  // L right and R + B NO WALLS
        AddConfig("Tiles_T_Side_04_C", O, O, W, L);  // Same as A
        AddConfig("Tiles_T_Side_04_D", W, L, O, O);  // R left and L + B NO WALLS

        // TCorner_01 series - single left/right openings on one edge
        AddConfig("Tiles_TCorner_01_A", L, W, W, W);  // F left
        AddConfig("Tiles_TCorner_01_B", R, W, W, W);  // F right
        AddConfig("Tiles_TCorner_01_C", W, W, R, W);  // B left (flipped to B right)
        AddConfig("Tiles_TCorner_01_D", W, W, L, W);  // B right (flipped to B left)

        // TCorner_02 series - single left/right openings on side edges
        AddConfig("Tiles_TCorner_02_A", W, W, W, L);  // L left
        AddConfig("Tiles_TCorner_02_B", W, W, W, R);  // L right
        AddConfig("Tiles_TCorner_02_C", W, R, W, W);  // R right
        AddConfig("Tiles_TCorner_02_D", W, L, W, W);  // R left

        // TCorner_03 series - one left/right opening + 2 open edges
        AddConfig("Tiles_TCorner_03_A", L, O, O, W);  // F left and R + B NO WALLS
        AddConfig("Tiles_TCorner_03_B", R, W, O, O);  // F right and L + B NO WALLS
        AddConfig("Tiles_TCorner_03_C", O, O, R, W);  // B left (flipped to B right) and F + R NO WALLS
        AddConfig("Tiles_TCorner_03_D", O, W, L, O);  // B right (flipped to B left) and L + F NO WALLS

        // TCorner_04 series - one left/right opening + 2 open edges (sides)
        AddConfig("Tiles_TCorner_04_A", O, O, W, L);  // L left and F + R NO WALLS
        AddConfig("Tiles_TCorner_04_B", W, O, O, R);  // L right and R + B NO WALLS
        AddConfig("Tiles_TCorner_04_C", O, R, W, O);  // R right and F + L NO WALLS
        AddConfig("Tiles_TCorner_04_D", W, L, O, O);  // R left and L + B NO WALLS

        // TCornerSide series - mixed left/right with center
        AddConfig("Tiles_TCornerSide_01_A", L, W, R, C);  // F left + L + B left (flipped to B right)
        AddConfig("Tiles_TCornerSide_01_B", R, C, L, W);  // F right + R + B right (flipped to B left)
        AddConfig("Tiles_TCornerSide_02_A", W, R, C, L);  // L left + R right + B
        AddConfig("Tiles_TCornerSide_02_B", C, L, W, R);  // F + L right + R left
        AddConfig("Tiles_TCornerSide_03_A", L, O, R, C);  // F left + L + B left (flipped to B right) and R NO WALLS
        AddConfig("Tiles_TCornerSide_03_B", R, C, L, O);  // F right + R + B right (flipped to B left) and L NO WALLS
        AddConfig("Tiles_TCornerSide_04_A", O, R, C, L);  // L left + R right + B and F NO WALLS
        AddConfig("Tiles_TCornerSide_04_B", C, L, O, R);  // L right + R Left + F and B NO WALLS

        // THalls series - open corridors
        AddConfig("Tiles_THalls_01_A", R, W, L, W);  // HALL on right side and F + B NO WALLS
        AddConfig("Tiles_THalls_01_B", L, W, R, W);  // HALL on left side and F + B NO WALLS
        AddConfig("Tiles_THalls_01_C", W, R, W, L);  // HALL on back side and R + L NO WALLS
        AddConfig("Tiles_THalls_01_D", W, L, W, R);  // HALL on front side and R + L NO WALLS

        // TStairs series - single left/right openings
        AddConfig("Tiles_TStairs_01_A", R, W, W, W);  // F right
        AddConfig("Tiles_TStairs_01_B", W, W, W, R);  // L right
        AddConfig("Tiles_TStairs_01_C", W, R, W, W);  // R right
        AddConfig("Tiles_TStairs_01_D", W, W, L, W);  // B left

        AddConfig("Tiles_TStairs_02_A", L, W, W, W);  // F left
        AddConfig("Tiles_TStairs_02_B", W, W, W, L);  // L left
        AddConfig("Tiles_TStairs_02_C", W, L, W, W);  // R left
        AddConfig("Tiles_TStairs_02_D", W, W, L, W);  // B right (flipped to B left)

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
        levelParents.Clear();
        stairsPositions.Clear();
        allLevelConfigs.Clear();
        pendingNavMeshLevels.Clear();

        if (Application.isPlaying)
        {
            // At runtime, defer NavMesh baking by one frame so Unity has time to register
            // all instantiated MeshRenderers before BuildNavMesh() reads the geometry.
            StartCoroutine(GenerateDungeonCoroutine());
        }
        else
        {
            // Editor button — geometry is already registered, bake immediately inside GenerateLevel.
            deferNavMesh = false;
            for (int level = 0; level < numberOfLevels; level++)
                GenerateLevel(level);
        }
    }

    private IEnumerator GenerateDungeonCoroutine()
    {
        // Place all tiles synchronously across every level (no baking yet)
        deferNavMesh = true;
        for (int level = 0; level < numberOfLevels; level++)
            GenerateLevel(level);
        deferNavMesh = false;

        // Wait one frame — Unity now registers all MeshRenderers from the Instantiate calls above
        yield return null;

        // Bake NavMesh for every level now that geometry is fully available
        DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
        if (navSetup != null)
        {
            foreach (var entry in pendingNavMeshLevels)
                navSetup.SetupLevel(entry.parent, entry.index, entry.configs,
                                    dungeonWidth, dungeonHeight, tileSize, levelHeight);
        }
        pendingNavMeshLevels.Clear();
    }

    void GenerateLevel(int levelIndex, int attemptNumber = 1)
    {
        Debug.Log($"=== Generating Level {levelIndex} (Attempt {attemptNumber}) ===");

        // Create parent for this level
        GameObject levelParent = new GameObject($"Level_{levelIndex}");
        levelParent.transform.parent = transform;
        levelParent.transform.position = new Vector3(0, levelIndex * -levelHeight, 0);
        levelParents.Add(levelParent);

        // Reset arrays for this level
        placedTiles = new GameObject[dungeonWidth, dungeonHeight];
        placedConfigs = new TileConfig[dungeonWidth, dungeonHeight];
        if (decorationObjects == null) decorationObjects = new List<GameObject>();

        // Determine starting position for this level
        int startX, startZ;
        if (levelIndex > 0 && stairsPositions.Count >= levelIndex)
        {
            // Start from where the previous level's stairs land
            Vector2Int landingPos = stairsPositions[levelIndex - 1];
            startX = landingPos.x;
            startZ = landingPos.y;
            Debug.Log($"Level {levelIndex}: Starting generation at stairs landing position ({startX},{startZ})");
        }
        else
        {
            // First level starts from center
            startX = dungeonWidth / 2;
            startZ = dungeonHeight / 2;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        int tilesPlaced = 0;

        // If starting from stairs landing, force a path inward from the edge
        if (levelIndex > 0 && stairsPositions.Count >= levelIndex)
        {
            // Determine which edge stairs are on and create path inward
            Vector2Int pathDir = Vector2Int.zero;
            if (startX == 0) pathDir = new Vector2Int(1, 0); // West edge - go East
            else if (startX == dungeonWidth - 1) pathDir = new Vector2Int(-1, 0); // East edge - go West
            else if (startZ == 0) pathDir = new Vector2Int(0, 1); // South edge - go North
            else if (startZ == dungeonHeight - 1) pathDir = new Vector2Int(0, -1); // North edge - go South

            // Force-place 3 tiles leading inward from stairs.
            // i=0 is the entrance tile (safe room) — it must use a room tile with only
            // Open/Wall edges so the safe room door prefab (centre opening) aligns correctly.
            for (int i = 0; i < 3; i++)
            {
                int px = startX + (pathDir.x * i);
                int pz = startZ + (pathDir.y * i);

                if (IsInBounds(px, pz))
                {
                    if (i == 0)
                        PlaceEntranceTile(px, pz, levelParent);
                    else
                        PlaceRandomRoomTile(px, pz, levelParent);

                    visited.Add(new Vector2Int(px, pz));
                    frontier.Enqueue(new Vector2Int(px, pz));
                    tilesPlaced++;
                }
            }

            Debug.Log($"Level {levelIndex}: Forced entrance path from edge in direction {pathDir}");
        }
        else
        {
            // First level - start from center normally
            PlaceRandomRoomTile(startX, startZ, levelParent);
            frontier.Enqueue(new Vector2Int(startX, startZ));
            visited.Add(new Vector2Int(startX, startZ));
            tilesPlaced = 1;
        }

        int attempts = 0, maxAttempts = targetTileCount * 15;

        while (frontier.Count > 0 && tilesPlaced < targetTileCount && attempts < maxAttempts)
        {
            attempts++;
            Vector2Int current = frontier.Dequeue();
            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y), new Vector2Int(current.x + 1, current.y),
                new Vector2Int(current.x, current.y + 1), new Vector2Int(current.x, current.y - 1)
            };

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

        // Run connectivity validation and repair with multiple passes
        Debug.Log($"Level {levelIndex}: Running connectivity validation...");
        List<Vector2Int> isolated = FindIsolatedTiles(startX, startZ);

        int repairPass = 0;
        int maxRepairPasses = 7; // Try up to 7 repair attempts for stubborn isolated areas

        isRepairing = true; // Relax placement rules during repair (e.g. sealed-from-dungeon checks)
        while (isolated.Count > 0 && repairPass < maxRepairPasses)
        {
            repairPass++;
            Debug.LogWarning($"Level {levelIndex}: Repair pass {repairPass} - Found {isolated.Count} isolated tiles");

            int repaired = 0;
            foreach (Vector2Int isoPos in isolated)
            {
                if (TryRepairIsolatedTile(isoPos.x, isoPos.y, startX, startZ, levelParent))
                {
                    repaired++;
                }
            }

            Debug.Log($"Level {levelIndex}: Pass {repairPass} repaired {repaired}/{isolated.Count} tiles");

            // Re-check connectivity after this pass
            isolated = FindIsolatedTiles(startX, startZ);

            if (isolated.Count == 0)
            {
                Debug.Log($"Level {levelIndex}: ✓ All tiles connected after {repairPass} pass(es)!");
                break;
            }
        }
        isRepairing = false;

        if (isolated.Count > 0)
        {
            Debug.LogWarning($"Level {levelIndex}: {isolated.Count} tiles remain isolated after {repairPass} passes");
            foreach (Vector2Int pos in isolated)
            {
                Debug.Log($"  - Isolated tile at ({pos.x},{pos.y})");
            }
        }

        // Run comprehensive validation
        if (!ValidateFullConnectivity(startX, startZ, levelParent, levelIndex))
        {
            if (attemptNumber < 3)
            {
                Debug.LogWarning($"Level {levelIndex} attempt {attemptNumber} failed validation. Retrying...");

                // Clear this failed attempt
                if (levelParent != null)
                {
                    DestroyImmediate(levelParent);
                }
                if (levelParents.Count > levelIndex)
                {
                    levelParents[levelIndex] = null;
                }

                // Retry generation
                GenerateLevel(levelIndex, attemptNumber + 1);
                return;
            }
            else
            {
                Debug.LogError($"Level {levelIndex} failed validation after 3 attempts. Using best attempt available.");
                // Continue with this level as the "best attempt"
            }
        }
        else
        {
            Debug.Log($"✓ Level {levelIndex} generated successfully on attempt {attemptNumber}");
        }

        AddDecorations(levelParent);
        PlaceStairsOnEdge(levelParent, levelIndex);

        SetupKeypads();

        // Save this level's configs for gizmos
        TileConfig[,] levelConfigsCopy = new TileConfig[dungeonWidth, dungeonHeight];
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
                levelConfigsCopy[x, z] = placedConfigs[x, z];
        allLevelConfigs[levelIndex] = levelConfigsCopy;

        // Setup NavMesh and NPC spawning for this level.
        // At runtime (deferNavMesh = true) the bake is deferred to GenerateDungeonCoroutine()
        // so it fires after Unity has registered all MeshRenderers from this frame's Instantiate calls.
        // In editor mode (deferNavMesh = false) bake immediately as before.
        if (!deferNavMesh)
        {
            DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
            if (navSetup != null)
                navSetup.SetupLevel(levelParent, levelIndex, levelConfigsCopy,
                                    dungeonWidth, dungeonHeight, tileSize, levelHeight);
            else
                Debug.LogError("DungeonNavMeshSetup component NOT FOUND!");
        }
        else
        {
            pendingNavMeshLevels.Add((levelParent, levelIndex, levelConfigsCopy));
        }

        // Seal all Wall edges with invisible collider barriers to prevent wall walk-through
        DungeonWallSealer sealer = FindObjectOfType<DungeonWallSealer>();
        if (sealer != null)
            sealer.SealLevel(this, levelIndex, levelParent);

        Debug.Log($"Level {levelIndex}: {tilesPlaced} tiles in {attempts} attempts");
    }


    bool IsInBounds(int x, int z) { return x >= 0 && x < dungeonWidth && z >= 0 && z < dungeonHeight; }

    void PlaceRandomRoomTile(int x, int z, GameObject parent)
    {
        // Get all room tiles that are compatible with perimeter constraints
        List<GameObject> roomTiles = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab != null && tileConfigs.ContainsKey(prefab.name) && !prefab.name.Contains("Stairs"))
            {
                TileConfig config = tileConfigs[prefab.name];
                if (config.IsRoomTile())
                {
                    // Check perimeter constraints with correct grid-to-tile mapping
                    bool validForPosition = true;
                    if (x == 0 && config.west != EdgeType.Wall) validForPosition = false;  // Left edge
                    if (x == dungeonWidth - 1 && config.east != EdgeType.Wall) validForPosition = false;  // Right edge
                    if (z == 0 && config.south != EdgeType.Wall) validForPosition = false;  // Back edge
                    if (z == dungeonHeight - 1 && config.north != EdgeType.Wall) validForPosition = false;  // Front edge

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

    // Variant of PlaceRandomRoomTile used specifically for the entrance tile (i=0 in the
    // forced path inward from stairs). Restricts to tiles whose edges are only Open or Wall —
    // no Left/Right/Center — so the safe room door prefab (which has a centre opening) always
    // aligns correctly. Falls back to a basic cross/hallway tile rather than TryPlaceCompatibleTile
    // to avoid any Left/Right tile slipping through the compatibility fallback.
    void PlaceEntranceTile(int x, int z, GameObject parent)
    {
        List<GameObject> candidates = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            TileConfig cfg = tileConfigs[prefab.name];

            // Must be a pure room tile: only Open or Wall on every edge
            if (!cfg.IsRoomTile()) continue;

            // Perimeter constraints
            if (x == 0 && cfg.west != EdgeType.Wall) continue;
            if (x == dungeonWidth - 1 && cfg.east != EdgeType.Wall) continue;
            if (z == 0 && cfg.south != EdgeType.Wall) continue;
            if (z == dungeonHeight - 1 && cfg.north != EdgeType.Wall) continue;

            candidates.Add(prefab);
        }

        if (candidates.Count > 0)
        {
            PlaceTile(x, z, candidates[Random.Range(0, candidates.Count)], parent);
            return;
        }

        // Hard fallback: find the first available room tile with no perimeter conflict
        // rather than calling TryPlaceCompatibleTile which could pick a Left/Right tile.
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            if (tileConfigs[prefab.name].IsRoomTile())
            {
                PlaceTile(x, z, prefab, parent);
                return;
            }
        }
    }

    bool TryPlaceCompatibleTile(int x, int z, GameObject parent)
    {
        // HYBRID APPROACH: Try strict rules first, then relaxed, then fill tile

        // Phase 1: Try STRICT matching (good pathing, proper rooms)
        List<GameObject> strictCompatibleTiles = new List<GameObject>();
        List<GameObject> strictRoomTiles = new List<GameObject>();

        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue; // Skip stairs during normal generation
            TileConfig config = tileConfigs[prefab.name];
            if (IsCompatibleWithNeighbors(x, z, config, strict: true))
            {
                if (config.IsRoomTile()) strictRoomTiles.Add(prefab);
                else strictCompatibleTiles.Add(prefab);
            }
        }

        GameObject chosen = null;

        // Try placing with strict rules
        if (strictRoomTiles.Count > 0 && Random.value < roomTileProbability)
            chosen = strictRoomTiles[Random.Range(0, strictRoomTiles.Count)];
        else if (strictCompatibleTiles.Count > 0)
            chosen = strictCompatibleTiles[Random.Range(0, strictCompatibleTiles.Count)];
        else if (strictRoomTiles.Count > 0)
            chosen = strictRoomTiles[Random.Range(0, strictRoomTiles.Count)];

        if (chosen != null)
        {
            PlaceTile(x, z, chosen, parent);
            return true;
        }

        // Phase 2: Try RELAXED matching (fill gaps with double-sided)
        List<GameObject> relaxedCompatibleTiles = new List<GameObject>();
        List<GameObject> relaxedRoomTiles = new List<GameObject>();

        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue; // Skip stairs during normal generation
            TileConfig config = tileConfigs[prefab.name];
            if (IsCompatibleWithNeighbors(x, z, config, strict: false))
            {
                if (config.IsRoomTile()) relaxedRoomTiles.Add(prefab);
                else relaxedCompatibleTiles.Add(prefab);
            }
        }

        // Try placing with relaxed rules
        if (relaxedRoomTiles.Count > 0 && Random.value < roomTileProbability)
            chosen = relaxedRoomTiles[Random.Range(0, relaxedRoomTiles.Count)];
        else if (relaxedCompatibleTiles.Count > 0)
            chosen = relaxedCompatibleTiles[Random.Range(0, relaxedCompatibleTiles.Count)];
        else if (relaxedRoomTiles.Count > 0)
            chosen = relaxedRoomTiles[Random.Range(0, relaxedRoomTiles.Count)];

        if (chosen != null)
        {
            PlaceTile(x, z, chosen, parent);
            Debug.Log($"Used relaxed matching at ({x},{z}) - {chosen.name}");
            return true;
        }

        // Phase 3: Fallback placement
        bool isPerimeter = (x == 0 || x == dungeonWidth - 1 || z == 0 || z == dungeonHeight - 1);

        if (isPerimeter)
        {
            // PERIMETER FALLBACK: Find any tile with Wall on outward edge(s)
            // A mismatched real tile is better than a fill (no walls) or empty gap
            List<GameObject> perimeterSafe = new List<GameObject>();
            foreach (GameObject prefab in allTilePrefabs)
            {
                if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
                if (prefab.name.Contains("Stairs") || prefab.name == "Tiles_01_Fill") continue;
                TileConfig cfg = tileConfigs[prefab.name];

                // Must have Wall on every outward-facing edge
                if (x == 0 && cfg.west != EdgeType.Wall) continue;
                if (x == dungeonWidth - 1 && cfg.east != EdgeType.Wall) continue;
                if (z == 0 && cfg.south != EdgeType.Wall) continue;
                if (z == dungeonHeight - 1 && cfg.north != EdgeType.Wall) continue;

                // Corner positions need Wall on both outward edges (already covered above)

                perimeterSafe.Add(prefab);
            }

            if (perimeterSafe.Count > 0)
            {
                GameObject perimFallback = perimeterSafe[Random.Range(0, perimeterSafe.Count)];
                PlaceTile(x, z, perimFallback, parent);
                Debug.LogWarning($"Perimeter fallback at ({x},{z}) - {perimFallback.name}");
                return false; // Don't expand frontier from fallback placement
            }
        }

        // Interior: use Fill tile as last resort
        if (!isPerimeter)
        {
            GameObject fillTile = System.Array.Find(allTilePrefabs, p => p != null && p.name == "Tiles_01_Fill");
            if (fillTile != null)
            {
                PlaceTile(x, z, fillTile, parent);
                Debug.LogWarning($"Used fill tile at ({x},{z}) - marked as dead end");
                return false; // Return FALSE to prevent frontier expansion
            }
        }

        return false;
    }

    bool IsCompatibleWithNeighbors(int x, int z, TileConfig config, bool strict = true)
    {
        // CRITICAL: Map grid edges to tile edges correctly
        // x=0 is LEFT edge (west), x=max is RIGHT edge (east)
        // z=0 is BACK edge (south), z=max is FRONT edge (north)

        // CRITICAL FIX: Fill tiles must NEVER be placed at perimeter
        // Fill tile config says (W,W,W,W) but has NO physical walls - creates walk-off hazard
        if (config.tileName == "Tiles_01_Fill")
        {
            bool isPerimeter = (x == 0 || x == dungeonWidth - 1 ||
                                z == 0 || z == dungeonHeight - 1);
            if (isPerimeter)
            {
                Debug.LogWarning($"Rejected Fill tile at ({x},{z}) - cannot place at perimeter (walk-off hazard)");
                return false;
            }
        }

        // Enhanced perimeter validation - check ALL outward-facing edges including corners
        if (x == 0)
        {
            if (config.west != EdgeType.Wall) return false;
            if (!isRepairing && config.east == EdgeType.Wall) return false; // sealed from dungeon (skip during repair)
            if (z == 0 && config.south != EdgeType.Wall) return false;
            if (z == dungeonHeight - 1 && config.north != EdgeType.Wall) return false;
        }
        if (x == dungeonWidth - 1)
        {
            if (config.east != EdgeType.Wall) return false;
            if (!isRepairing && config.west == EdgeType.Wall) return false; // sealed from dungeon (skip during repair)
            if (z == 0 && config.south != EdgeType.Wall) return false;
            if (z == dungeonHeight - 1 && config.north != EdgeType.Wall) return false;
        }
        if (z == 0)
        {
            if (config.south != EdgeType.Wall) return false;
            if (!isRepairing && x > 0 && x < dungeonWidth - 1 && config.north == EdgeType.Wall) return false; // sealed from dungeon (skip during repair)
        }
        if (z == dungeonHeight - 1)
        {
            if (config.north != EdgeType.Wall) return false;
            if (!isRepairing && x > 0 && x < dungeonWidth - 1 && config.south == EdgeType.Wall) return false; // sealed from dungeon (skip during repair)
        }

        // Additional check: Prevent tiles with 3+ openings near perimeter (they create walk-off areas)
        // These tiles need to be surrounded by other tiles to prevent walk-off points
        int openingCount = config.GetOpeningCount();
        if (openingCount >= 3)
        {
            // Block near-edge positions (within 2 tiles of any edge)
            bool isNearEdge = (x <= 1 || x >= dungeonWidth - 2 ||
                               z <= 1 || z >= dungeonHeight - 2);

            if (isNearEdge)
            {
                // Only allow if completely surrounded by placed tiles
                bool hasAllNeighbors = true;
                if (x > 0 && placedTiles[x - 1, z] == null) hasAllNeighbors = false;
                if (x < dungeonWidth - 1 && placedTiles[x + 1, z] == null) hasAllNeighbors = false;
                if (z > 0 && placedTiles[x, z - 1] == null) hasAllNeighbors = false;
                if (z < dungeonHeight - 1 && placedTiles[x, z + 1] == null) hasAllNeighbors = false;

                if (!hasAllNeighbors)
                {
                    Debug.Log($"Rejected {config.tileName} at ({x},{z}) - {openingCount} openings near edge");
                    return false;
                }
            }
        }

        // Prevent dead-end rooms: Don't place single-opening tiles adjacent to other single-opening tiles
        if (config.GetOpeningCount() == 1)
        {
            // Check each neighbor - if they also have only 1 opening, reject this placement
            if (x > 0 && placedConfigs[x - 1, z] != null && placedConfigs[x - 1, z].GetOpeningCount() == 1)
                return false; // West neighbor has 1 opening - would create 2-tile dead end
            if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && placedConfigs[x + 1, z].GetOpeningCount() == 1)
                return false; // East neighbor has 1 opening - would create 2-tile dead end
            if (z > 0 && placedConfigs[x, z - 1] != null && placedConfigs[x, z - 1].GetOpeningCount() == 1)
                return false; // South neighbor has 1 opening - would create 2-tile dead end
            if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && placedConfigs[x, z + 1].GetOpeningCount() == 1)
                return false; // North neighbor has 1 opening - would create 2-tile dead end
        }

        // Prevent 4-corner closed loops: Don't allow 2-opening tiles (BasicCorner) to be adjacent to each other
        // This prevents them from forming closed 2x2 squares with no exit
        if (config.GetOpeningCount() == 2)
        {
            // Count how many neighbors are also 2-opening tiles
            int twoOpeningNeighbors = 0;

            if (x > 0 && placedConfigs[x - 1, z] != null && placedConfigs[x - 1, z].GetOpeningCount() == 2)
                twoOpeningNeighbors++;
            if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && placedConfigs[x + 1, z].GetOpeningCount() == 2)
                twoOpeningNeighbors++;
            if (z > 0 && placedConfigs[x, z - 1] != null && placedConfigs[x, z - 1].GetOpeningCount() == 2)
                twoOpeningNeighbors++;
            if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && placedConfigs[x, z + 1].GetOpeningCount() == 2)
                twoOpeningNeighbors++;

            // If 2+ neighbors are also 2-opening tiles, reject to prevent closed loop formation
            if (twoOpeningNeighbors >= 2)
            {
                Debug.Log($"Rejected {config.tileName} at ({x},{z}) - would form closed loop with {twoOpeningNeighbors} 2-opening neighbors");
                return false;
            }
        }

        // Prevent Left/Right tiles from forming isolated chains
        // THalls + TCorner combinations can create 3-tile isolated groups
        bool hasLeftOrRight = (config.north == EdgeType.Left || config.north == EdgeType.Right ||
                               config.south == EdgeType.Left || config.south == EdgeType.Right ||
                               config.east == EdgeType.Left || config.east == EdgeType.Right ||
                               config.west == EdgeType.Left || config.west == EdgeType.Right);

        if (hasLeftOrRight)
        {
            int singleOpeningLRNeighbors = 0;

            // Check all 4 neighbors for single-opening Left/Right tiles
            if (x > 0 && placedConfigs[x - 1, z] != null)
            {
                TileConfig neighbor = placedConfigs[x - 1, z];
                bool neighborHasLR = (neighbor.north == EdgeType.Left || neighbor.north == EdgeType.Right ||
                                      neighbor.south == EdgeType.Left || neighbor.south == EdgeType.Right ||
                                      neighbor.east == EdgeType.Left || neighbor.east == EdgeType.Right ||
                                      neighbor.west == EdgeType.Left || neighbor.west == EdgeType.Right);
                if (neighborHasLR && neighbor.GetOpeningCount() == 1)
                    singleOpeningLRNeighbors++;
            }

            if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null)
            {
                TileConfig neighbor = placedConfigs[x + 1, z];
                bool neighborHasLR = (neighbor.north == EdgeType.Left || neighbor.north == EdgeType.Right ||
                                      neighbor.south == EdgeType.Left || neighbor.south == EdgeType.Right ||
                                      neighbor.east == EdgeType.Left || neighbor.east == EdgeType.Right ||
                                      neighbor.west == EdgeType.Left || neighbor.west == EdgeType.Right);
                if (neighborHasLR && neighbor.GetOpeningCount() == 1)
                    singleOpeningLRNeighbors++;
            }

            if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null)
            {
                TileConfig neighbor = placedConfigs[x, z + 1];
                bool neighborHasLR = (neighbor.north == EdgeType.Left || neighbor.north == EdgeType.Right ||
                                      neighbor.south == EdgeType.Left || neighbor.south == EdgeType.Right ||
                                      neighbor.east == EdgeType.Left || neighbor.east == EdgeType.Right ||
                                      neighbor.west == EdgeType.Left || neighbor.west == EdgeType.Right);
                if (neighborHasLR && neighbor.GetOpeningCount() == 1)
                    singleOpeningLRNeighbors++;
            }

            if (z > 0 && placedConfigs[x, z - 1] != null)
            {
                TileConfig neighbor = placedConfigs[x, z - 1];
                bool neighborHasLR = (neighbor.north == EdgeType.Left || neighbor.north == EdgeType.Right ||
                                      neighbor.south == EdgeType.Left || neighbor.south == EdgeType.Right ||
                                      neighbor.east == EdgeType.Left || neighbor.east == EdgeType.Right ||
                                      neighbor.west == EdgeType.Left || neighbor.west == EdgeType.Right);
                if (neighborHasLR && neighbor.GetOpeningCount() == 1)
                    singleOpeningLRNeighbors++;
            }

            // Reject patterns that create isolated chains
            if (openingCount == 1 && singleOpeningLRNeighbors >= 1)
            {
                // TCorner-style tile next to another TCorner-style tile
                Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - L/R tiles clustering (would form isolated chain)");
                return false;
            }

            if (openingCount == 2 && singleOpeningLRNeighbors >= 2)
            {
                // THalls-style tile trapped between two TCorner-style tiles
                Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - would create L/R dead-end chain");
                return false;
            }

            // Extra check: placing a single-opening L/R tile next to a 2-opening L/R tile (THalls)
            // that already has a single-opening L/R tile on its OTHER end would seal the chain
            if (openingCount == 1)
            {
                Vector2Int[] dirs = {
                    new Vector2Int(x - 1, z), new Vector2Int(x + 1, z),
                    new Vector2Int(x, z + 1), new Vector2Int(x, z - 1)
                };
                foreach (Vector2Int d in dirs)
                {
                    if (!IsInBounds(d.x, d.y) || placedConfigs[d.x, d.y] == null) continue;
                    TileConfig adj = placedConfigs[d.x, d.y];
                    bool adjHasLR = (adj.north == EdgeType.Left || adj.north == EdgeType.Right ||
                                     adj.south == EdgeType.Left || adj.south == EdgeType.Right ||
                                     adj.east == EdgeType.Left || adj.east == EdgeType.Right ||
                                     adj.west == EdgeType.Left || adj.west == EdgeType.Right);
                    if (!adjHasLR || adj.GetOpeningCount() != 2) continue;

                    // This neighbor is a 2-opening L/R tile (THalls-style)
                    // Check if its OTHER neighbors already include a single-opening L/R tile
                    Vector2Int[] adjDirs = {
                        new Vector2Int(d.x - 1, d.y), new Vector2Int(d.x + 1, d.y),
                        new Vector2Int(d.x, d.y + 1), new Vector2Int(d.x, d.y - 1)
                    };
                    foreach (Vector2Int ad in adjDirs)
                    {
                        if (ad.x == x && ad.y == z) continue; // Skip ourselves
                        if (!IsInBounds(ad.x, ad.y) || placedConfigs[ad.x, ad.y] == null) continue;
                        TileConfig farNeighbor = placedConfigs[ad.x, ad.y];
                        bool farHasLR = (farNeighbor.north == EdgeType.Left || farNeighbor.north == EdgeType.Right ||
                                         farNeighbor.south == EdgeType.Left || farNeighbor.south == EdgeType.Right ||
                                         farNeighbor.east == EdgeType.Left || farNeighbor.east == EdgeType.Right ||
                                         farNeighbor.west == EdgeType.Left || farNeighbor.west == EdgeType.Right);
                        if (farHasLR && farNeighbor.GetOpeningCount() == 1)
                        {
                            Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - would seal L/R chain through ({d.x},{d.y})");
                            return false;
                        }
                    }
                }
            }
        }

        // Check compatibility with placed neighbors
        // Neighbor at (x-1,z) is to the WEST/LEFT
        if (x > 0 && placedConfigs[x - 1, z] != null && !EdgesMatch(config.west, placedConfigs[x - 1, z].east, strict)) return false;
        // Neighbor at (x+1,z) is to the EAST/RIGHT
        if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && !EdgesMatch(config.east, placedConfigs[x + 1, z].west, strict)) return false;
        // Neighbor at (x,z+1) is to the NORTH/FRONT
        if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && !EdgesMatch(config.north, placedConfigs[x, z + 1].south, strict)) return false;
        // Neighbor at (x,z-1) is to the SOUTH/BACK
        if (z > 0 && placedConfigs[x, z - 1] != null && !EdgesMatch(config.south, placedConfigs[x, z - 1].north, strict)) return false;
        return true;
    }

    bool EdgesMatch(EdgeType mine, EdgeType theirs, bool strict = true)
    {
        if (strict)
        {
            // STRICT RULES - for good pathing and proper room/corridor separation

            // Wall to Wall = OK
            if (mine == EdgeType.Wall && theirs == EdgeType.Wall)
                return true;

            // Wall to anything else = REJECT (in strict mode)
            if (mine == EdgeType.Wall || theirs == EdgeType.Wall)
                return false;

            // Open ONLY matches Open
            if (mine == EdgeType.Open || theirs == EdgeType.Open)
                return mine == EdgeType.Open && theirs == EdgeType.Open;

            // Center to Center
            if (mine == EdgeType.Center && theirs == EdgeType.Center)
                return true;

            // Left to Right (mirrored)
            if (mine == EdgeType.Left && theirs == EdgeType.Right)
                return true;
            if (mine == EdgeType.Right && theirs == EdgeType.Left)
                return true;

            // Reject misaligned openings
            return false;
        }
        else
        {
            // RELAXED RULES - fallback using double-sided walls to fill gaps

            // Wall to Wall = OK
            if (mine == EdgeType.Wall && theirs == EdgeType.Wall)
                return true;

            // Wall to Open = OK (double-sided fallback)
            if ((mine == EdgeType.Wall && theirs == EdgeType.Open) ||
                (mine == EdgeType.Open && theirs == EdgeType.Wall))
                return true;

            // Wall to Center/Left/Right = REJECT (wall blocks opening)
            if (mine == EdgeType.Wall || theirs == EdgeType.Wall)
                return false;

            // Open matches anything non-Wall (relaxed)
            if (mine == EdgeType.Open || theirs == EdgeType.Open)
                return true;

            // Center to Center
            if (mine == EdgeType.Center && theirs == EdgeType.Center)
                return true;

            // Left to Right (mirrored)
            if (mine == EdgeType.Left && theirs == EdgeType.Right)
                return true;
            if (mine == EdgeType.Right && theirs == EdgeType.Left)
                return true;

            // Reject misaligned openings
            return false;
        }
    }

    void PlaceTile(int x, int z, GameObject prefab, GameObject parent)
    {
        Vector3 pos = new Vector3(x * tileSize, parent.transform.position.y, z * tileSize);
        placedTiles[x, z] = Instantiate(prefab, pos, Quaternion.identity, parent.transform);
        placedTiles[x, z].name = $"Tile_{x}_{z}_{prefab.name}";
        if (tileConfigs.ContainsKey(prefab.name)) placedConfigs[x, z] = tileConfigs[prefab.name];
    }

    void AddDecorations(GameObject parent)
    {
        if (decorationObjects == null) decorationObjects = new List<GameObject>();

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedConfigs[x, z] == null) continue;
                TileConfig config = placedConfigs[x, z];
                Vector3 tilePos = new Vector3(x * tileSize, parent.transform.position.y, z * tileSize);

                if (pillerPrefab != null && config.IsFullyOpen() && Random.value < pillarChance)
                {
                    GameObject pillar = Instantiate(pillerPrefab, tilePos, Quaternion.identity, parent.transform);
                    pillar.name = $"Pillar_{x}_{z}";
                    decorationObjects.Add(pillar);
                }

                if (wallDoorPrefab != null && Random.value < doorChance && !config.IsRoomTile())
                {
                    if (x > 0 && placedConfigs[x - 1, z] != null && placedConfigs[x - 1, z].IsRoomTile())
                    {
                        Vector3 doorPos = tilePos + new Vector3(0, 0, -tileSize / 4);
                        GameObject door = Instantiate(wallDoorPrefab, doorPos, Quaternion.identity, parent.transform);
                        door.name = $"Door_{x}_{z}_N";
                        decorationObjects.Add(door);
                    }
                }
            }
        }

        Debug.Log($"Added {decorationObjects.Count} decorations");
    }

    void PlaceStairsOnEdge(GameObject parent, int levelIndex)
    {
        // Find all stairs tiles
        List<GameObject> stairsPrefabs = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab != null && prefab.name.Contains("Stairs"))
                stairsPrefabs.Add(prefab);
        }

        if (stairsPrefabs.Count == 0) return;

        // Collect all valid (position, stairs_tile) pairs on perimeter
        List<(int x, int z, GameObject prefab)> validPlacements = new List<(int, int, GameObject)>();

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                // Only check perimeter positions
                if (x != 0 && x != dungeonWidth - 1 && z != 0 && z != dungeonHeight - 1) continue;
                if (placedTiles[x, z] == null) continue;

                // Try each stairs tile
                foreach (GameObject stairsPrefab in stairsPrefabs)
                {
                    if (!tileConfigs.ContainsKey(stairsPrefab.name)) continue;
                    TileConfig stairsConfig = tileConfigs[stairsPrefab.name];

                    if (IsCompatibleWithNeighbors(x, z, stairsConfig, strict: false))
                    {
                        validPlacements.Add((x, z, stairsPrefab));
                    }
                }
            }
        }

        // Pick one random valid placement
        if (validPlacements.Count > 0)
        {
            var chosen = validPlacements[Random.Range(0, validPlacements.Count)];

            // Remove old tile
            if (placedTiles[chosen.x, chosen.z] != null)
                (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(placedTiles[chosen.x, chosen.z]);

            // Place stairs at the NEXT level down so player walks down them
            float stairsY = (levelIndex + 1) * -levelHeight; // One level below current
            Vector3 stairsPos = new Vector3(chosen.x * tileSize, stairsY, chosen.z * tileSize);
            placedTiles[chosen.x, chosen.z] = Instantiate(chosen.prefab, stairsPos, Quaternion.identity, parent.transform);
            placedTiles[chosen.x, chosen.z].name = $"Tile_{chosen.x}_{chosen.z}_{chosen.prefab.name}";
            if (tileConfigs.ContainsKey(chosen.prefab.name)) placedConfigs[chosen.x, chosen.z] = tileConfigs[chosen.prefab.name];

            // Save stairs position for next level to use as entrance
            stairsPositions.Add(new Vector2Int(chosen.x, chosen.z));
            Debug.Log($"Level {levelIndex}: Placed stairs at edge ({chosen.x},{chosen.z}) - {chosen.prefab.name}");
        }
        else
        {
            Debug.LogWarning($"Level {levelIndex}: Could not find valid position for stairs on edge");
        }
    }

    [ContextMenu("Validate Dungeon")]
    void ValidateDungeon()
    {
        if (placedConfigs == null)
        {
            Debug.LogWarning("No dungeon to validate - generate one first");
            return;
        }

        int errorCount = 0;
        Debug.Log("=== VALIDATING DUNGEON ===");

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedConfigs[x, z] == null) continue;
                TileConfig config = placedConfigs[x, z];
                string tileName = placedTiles[x, z] != null ? placedTiles[x, z].name : "Unknown";

                // Check perimeter with correct grid-to-tile mapping
                if (x == 0 && config.west != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - West edge (left) should be Wall, got {config.west}");
                    errorCount++;
                }
                if (x == dungeonWidth - 1 && config.east != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - East edge (right) should be Wall, got {config.east}");
                    errorCount++;
                }
                if (z == 0 && config.south != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - South edge (back) should be Wall, got {config.south}");
                    errorCount++;
                }
                if (z == dungeonHeight - 1 && config.north != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - North edge (front) should be Wall, got {config.north}");
                    errorCount++;
                }

                // Check neighbor connections with correct grid mapping (using STRICT rules)
                if (x > 0 && placedConfigs[x - 1, z] != null)
                {
                    if (!EdgesMatch(config.west, placedConfigs[x - 1, z].east, strict: true))
                    {
                        // Check if it would pass with relaxed rules
                        if (EdgesMatch(config.west, placedConfigs[x - 1, z].east, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - West:{config.west} vs left neighbor East:{placedConfigs[x - 1, z].east}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - West:{config.west} vs left neighbor East:{placedConfigs[x - 1, z].east}");
                            errorCount++;
                        }
                    }
                }
                if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null)
                {
                    if (!EdgesMatch(config.east, placedConfigs[x + 1, z].west, strict: true))
                    {
                        if (EdgesMatch(config.east, placedConfigs[x + 1, z].west, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - East:{config.east} vs right neighbor West:{placedConfigs[x + 1, z].west}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - East:{config.east} vs right neighbor West:{placedConfigs[x + 1, z].west}");
                            errorCount++;
                        }
                    }
                }
                if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null)
                {
                    if (!EdgesMatch(config.north, placedConfigs[x, z + 1].south, strict: true))
                    {
                        if (EdgesMatch(config.north, placedConfigs[x, z + 1].south, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - North:{config.north} vs front neighbor South:{placedConfigs[x, z + 1].south}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - North:{config.north} vs front neighbor South:{placedConfigs[x, z + 1].south}");
                            errorCount++;
                        }
                    }
                }
                if (z > 0 && placedConfigs[x, z - 1] != null)
                {
                    if (!EdgesMatch(config.south, placedConfigs[x, z - 1].north, strict: true))
                    {
                        if (EdgesMatch(config.south, placedConfigs[x, z - 1].north, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - South:{config.south} vs back neighbor North:{placedConfigs[x, z - 1].north}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - South:{config.south} vs back neighbor North:{placedConfigs[x, z - 1].north}");
                            errorCount++;
                        }
                    }
                }
            }
        }

        Debug.Log("=== VALIDATION COMPLETE ===");
        if (errorCount == 0)
            Debug.Log("✓ PASSED - No illegal placements found!");
        else
            Debug.LogError($"✗ FAILED - Found {errorCount} illegal placements!");
    }

    // ══════════════════════════════════════════
    // CONNECTIVITY VALIDATION & REPAIR METHODS
    // ══════════════════════════════════════════

    bool IsPassableEdge(EdgeType edge)
    {
        // Only Wall edges block movement
        return edge != EdgeType.Wall;
    }

    bool CanWalkBetween(int x1, int z1, int x2, int z2)
    {
        // Determine direction and check if both tiles have passable edges
        TileConfig tile1 = placedConfigs[x1, z1];
        TileConfig tile2 = placedConfigs[x2, z2];

        if (tile1 == null || tile2 == null) return false;

        // Fill tiles are physically open floor (no wall meshes) despite their (W,W,W,W) config.
        // BUT the neighboring real tile's wall still physically blocks passage.
        // So we only skip the fill tile's own edge check — the real tile's edge still matters.
        bool tile1IsFill = (tile1.tileName == "Tiles_01_Fill");
        bool tile2IsFill = (tile2.tileName == "Tiles_01_Fill");

        // Both fills = both open floor, no walls anywhere, always passable
        if (tile1IsFill && tile2IsFill)
            return true;

        // Get the edges each tile has facing the other
        EdgeType edge1Facing, edge2Facing;

        if (x2 == x1 - 1) { edge1Facing = tile1.west; edge2Facing = tile2.east; }       // tile2 is West
        else if (x2 == x1 + 1) { edge1Facing = tile1.east; edge2Facing = tile2.west; }   // tile2 is East
        else if (z2 == z1 + 1) { edge1Facing = tile1.north; edge2Facing = tile2.south; }  // tile2 is North
        else if (z2 == z1 - 1) { edge1Facing = tile1.south; edge2Facing = tile2.north; }  // tile2 is South
        else return false;

        // If one tile is fill, only check the OTHER tile's edge (fill has no physical walls)
        if (tile1IsFill) return IsPassableEdge(edge2Facing);
        if (tile2IsFill) return IsPassableEdge(edge1Facing);

        // Normal case: both tiles must have passable edges facing each other
        return IsPassableEdge(edge1Facing) && IsPassableEdge(edge2Facing);
    }

    // ── Public accessors for CodeNumberManager ──────────────────────────────
    // Returns all walkable, non-fill tile grid positions reachable from spawn.
    public List<Vector2Int> GetReachableTilePositions(int startX, int startZ)
    {
        HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Vector2Int start = new Vector2Int(startX, startZ);
        queue.Enqueue(start);
        reachable.Add(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y),
                new Vector2Int(current.x + 1, current.y),
                new Vector2Int(current.x, current.y + 1),
                new Vector2Int(current.x, current.y - 1)
            };
            foreach (Vector2Int n in neighbors)
            {
                if (!IsInBounds(n.x, n.y) || reachable.Contains(n)) continue;
                if (placedTiles[n.x, n.y] == null) continue;
                if (CanWalkBetween(current.x, current.y, n.x, n.y))
                {
                    reachable.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        // Exclude fill tiles — they have no physical walls and are dead zones.
        List<Vector2Int> result = new List<Vector2Int>();
        foreach (Vector2Int pos in reachable)
        {
            TileConfig cfg = placedConfigs[pos.x, pos.y];
            if (cfg != null && cfg.tileName != "Tiles_01_Fill")
                result.Add(pos);
        }
        return result;
    }

    public TileConfig GetTileConfig(int x, int z) => placedConfigs?[x, z];
    public GameObject GetPlacedTile(int x, int z) => placedTiles?[x, z];
    public float TileSize => tileSize;
    public float LevelHeight => levelHeight;
    public int DungeonWidth => dungeonWidth;
    public int DungeonHeight => dungeonHeight;
    public List<Vector2Int> StairsPositions => stairsPositions;
    // ────────────────────────────────────────────────────────────────────────

    List<Vector2Int> FindIsolatedTiles(int startX, int startZ)
    {
        // Flood fill from start position to find all reachable tiles
        HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startZ));
        reachable.Add(new Vector2Int(startX, startZ));

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            // Check all 4 neighbors
            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y), // West
                new Vector2Int(current.x + 1, current.y), // East
                new Vector2Int(current.x, current.y + 1), // North
                new Vector2Int(current.x, current.y - 1)  // South
            };

            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2Int n = neighbors[i];
                if (!IsInBounds(n.x, n.y)) continue;
                if (reachable.Contains(n)) continue;
                if (placedTiles[n.x, n.y] == null) continue;

                // Check if there's a valid path between current and neighbor
                if (CanWalkBetween(current.x, current.y, n.x, n.y))
                {
                    reachable.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        // Find all placed tiles that aren't reachable
        List<Vector2Int> isolated = new List<Vector2Int>();
        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedTiles[x, z] != null && !reachable.Contains(new Vector2Int(x, z)))
                {
                    isolated.Add(new Vector2Int(x, z));
                }
            }
        }

        return isolated;
    }

    bool TryRepairIsolatedTile(int targetX, int targetZ, int startX, int startZ, GameObject parent)
    {
        TileConfig targetConfig = placedConfigs[targetX, targetZ];
        if (targetConfig == null) return false;

        // Strategy 1: If the isolated tile is a fill tile, try replacing it with a real tile
        if (targetConfig.tileName == "Tiles_01_Fill")
        {
            // Remove the fill tile
            if (placedTiles[targetX, targetZ] != null)
                (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(placedTiles[targetX, targetZ]);
            placedTiles[targetX, targetZ] = null;
            placedConfigs[targetX, targetZ] = null;

            // Try to place a real tile that creates connections
            bool placed = TryPlaceCompatibleTile(targetX, targetZ, parent);
            if (placed)
                return true;

            // Direct replacement failed — all neighbors have Wall facing us so no tile fits.
            // Don't restore the fill — an empty slot is better than an isolated fill tile.
            // Fall through to Strategy 2 to try replacing a blocking neighbor instead.
        }

        // Strategy 2: Find which neighbors block passage and try replacing them
        // BFS outward from isolated tile to find adjacent tiles that block walkability
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        queue.Enqueue(new Vector2Int(targetX, targetZ));
        visited.Add(new Vector2Int(targetX, targetZ));

        // Collect candidates: tiles adjacent to our isolated cluster that block passage
        List<Vector2Int> blockingCandidates = new List<Vector2Int>();

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y),
                new Vector2Int(current.x + 1, current.y),
                new Vector2Int(current.x, current.y + 1),
                new Vector2Int(current.x, current.y - 1)
            };

            foreach (Vector2Int n in neighbors)
            {
                if (!IsInBounds(n.x, n.y)) continue;
                if (visited.Contains(n)) continue;
                if (placedTiles[n.x, n.y] == null) continue;

                visited.Add(n);

                if (!CanWalkBetween(current.x, current.y, n.x, n.y))
                {
                    blockingCandidates.Add(n);
                }
                else
                {
                    queue.Enqueue(n);
                }
            }
        }

        // Try replacing each blocking candidate until one works
        foreach (Vector2Int blocker in blockingCandidates)
        {
            int bx = blocker.x;
            int bz = blocker.y;

            // Save old tile in case replacement fails
            GameObject oldTileObj = placedTiles[bx, bz];
            TileConfig oldConfig = placedConfigs[bx, bz];
            string oldPrefabName = oldConfig != null ? oldConfig.tileName : null;

            // Remove blocking tile
            if (oldTileObj != null)
                (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(oldTileObj);
            placedTiles[bx, bz] = null;
            placedConfigs[bx, bz] = null;

            // Try to place a better tile
            if (TryPlaceCompatibleTile(bx, bz, parent))
            {
                // Check if this actually fixes connectivity
                List<Vector2Int> stillIsolated = FindIsolatedTiles(startX, startZ);
                bool targetStillIsolated = stillIsolated.Contains(new Vector2Int(targetX, targetZ));
                if (!targetStillIsolated)
                {
                    Debug.Log($"Repair: replaced ({bx},{bz}) to connect ({targetX},{targetZ})");
                    return true;
                }
                // Didn't fix it — undo and try next candidate
                if (placedTiles[bx, bz] != null)
                    (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(placedTiles[bx, bz]);
            }

            // Restore old tile
            placedTiles[bx, bz] = null;
            placedConfigs[bx, bz] = null;
            if (oldPrefabName != null)
            {
                GameObject prefab = System.Array.Find(allTilePrefabs, p => p != null && p.name == oldPrefabName);
                if (prefab != null)
                    PlaceTile(bx, bz, prefab, parent);
            }
        }

        return false;
    }

    bool ValidateFullConnectivity(int startX, int startZ, GameObject parent, int levelIndex)
    {
        int errorCount = 0;

        // 1. Check all tiles are reachable
        List<Vector2Int> isolated = FindIsolatedTiles(startX, startZ);
        if (isolated.Count > 0)
        {
            Debug.LogError($"✗ Level {levelIndex} validation FAILED: {isolated.Count} isolated tiles");
            foreach (Vector2Int pos in isolated)
            {
                Debug.Log($"    - Isolated tile at ({pos.x},{pos.y})");
            }
            errorCount += isolated.Count;
        }

        // 2. Check perimeter for walk-off points
        int walkoffCount = 0;
        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedTiles[x, z] == null) continue;
                TileConfig config = placedConfigs[x, z];

                // CRITICAL: Check for Fill tiles at perimeter (config/visual mismatch)
                if (config.tileName == "Tiles_01_Fill")
                {
                    bool isPerimeter = (x == 0 || x == dungeonWidth - 1 ||
                                        z == 0 || z == dungeonHeight - 1);
                    if (isPerimeter)
                    {
                        Debug.LogError($"✗ Fill tile at perimeter ({x},{z}) - walk-off hazard! (config says Wall but no physical walls)");
                        walkoffCount++;
                    }
                }

                // Check edges for non-wall openings facing outward
                if (x == 0 && config.west != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): West edge is {config.west}");
                    walkoffCount++;
                }
                if (x == dungeonWidth - 1 && config.east != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): East edge is {config.east}");
                    walkoffCount++;
                }
                if (z == 0 && config.south != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): South edge is {config.south}");
                    walkoffCount++;
                }
                if (z == dungeonHeight - 1 && config.north != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): North edge is {config.north}");
                    walkoffCount++;
                }
            }
        }
        errorCount += walkoffCount;

        // 3. Check minimum tile count reached (at least 80% of target)
        int tileCount = 0;
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
                if (placedTiles[x, z] != null) tileCount++;

        if (tileCount < targetTileCount * 0.8f)
        {
            Debug.LogError($"✗ Level {levelIndex} validation FAILED: Only {tileCount}/{targetTileCount} tiles (need at least 80%)");
            errorCount++;
        }

        // Final result
        if (errorCount == 0)
        {
            Debug.Log($"✓ Level {levelIndex} validation PASSED: {tileCount} tiles, all connected, no walk-offs");
            return true;
        }
        else
        {
            Debug.LogError($"✗ Level {levelIndex} validation FAILED: {errorCount} total errors");
            return false;
        }
    }

    [ContextMenu("Clear Dungeon")]
    public void ClearDungeon()
    {
        // Clear NPC spawns first
        DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
        if (navSetup != null)
            navSetup.ClearAllSpawns();

        // Clear all level parents and their children
        if (levelParents != null)
        {
            foreach (GameObject levelParent in levelParents)
            {
                if (levelParent != null)
                    (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(levelParent);
            }
            levelParents.Clear();
        }

        // Clear old single-level tiles (for backwards compatibility)
        if (placedTiles != null)
            for (int x = 0; x < placedTiles.GetLength(0); x++)
                for (int z = 0; z < placedTiles.GetLength(1); z++)
                    if (placedTiles[x, z] != null)
                        (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(placedTiles[x, z]);

        if (decorationObjects != null)
        {
            foreach (GameObject decoration in decorationObjects)
                if (decoration != null)
                    (Application.isPlaying ? (System.Action<Object>)Destroy : DestroyImmediate)(decoration);
            decorationObjects.Clear();
        }

        // Clear all multi-level data
        allLevelConfigs.Clear();
        stairsPositions.Clear();

        placedTiles = null;
        placedConfigs = null;
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || allLevelConfigs.Count == 0) return;

        foreach (var kvp in allLevelConfigs)
        {
            int levelIndex = kvp.Key;
            TileConfig[,] configs = kvp.Value;
            float levelY = levelIndex * -levelHeight;

            for (int x = 0; x < dungeonWidth; x++)
            {
                for (int z = 0; z < dungeonHeight; z++)
                {
                    if (configs[x, z] == null) continue;
                    TileConfig config = configs[x, z];
                    Vector3 centerPos = new Vector3(x * tileSize, levelY + 0.5f, z * tileSize);

                    Gizmos.color = config.IsRoomTile() ? new Color(0.5f, 0.8f, 1f, 0.5f) : new Color(1f, 0.9f, 0.3f, 0.5f);
                    Gizmos.DrawWireCube(centerPos, Vector3.one * (tileSize * 0.9f));

                    float lineLength = tileSize * 0.35f;
                    if (config.north != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.north); Gizmos.DrawLine(centerPos, centerPos + Vector3.forward * lineLength); }
                    if (config.east != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.east); Gizmos.DrawLine(centerPos, centerPos + Vector3.right * lineLength); }
                    if (config.south != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.south); Gizmos.DrawLine(centerPos, centerPos + Vector3.back * lineLength); }
                    if (config.west != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.west); Gizmos.DrawLine(centerPos, centerPos + Vector3.left * lineLength); }
                }
            }
        }
    }

    Color GetEdgeColor(EdgeType edge)
    {
        switch (edge)
        {
            case EdgeType.Center: return Color.green;
            case EdgeType.Left: return Color.cyan;
            case EdgeType.Right: return Color.magenta;
            case EdgeType.Open: return Color.yellow;
            default: return Color.red;
        }
    }

    void SetupKeypads()
    {
        // Find player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("Player not found! Make sure player has 'Player' tag");
            return;
        }

        // Find the UI prompt
        GameObject promptUI = GameObject.Find("KeypadPrompt");
        TMPro.TMP_Text promptText = promptUI != null ? promptUI.GetComponent<TMPro.TMP_Text>() : null;

        // Find all keypads
        NavKeypad.KeypadPlayerInteraction[] keypads = FindObjectsOfType<NavKeypad.KeypadPlayerInteraction>();

        foreach (var kp in keypads)
        {
            // Set player reference
            typeof(NavKeypad.KeypadPlayerInteraction)
                .GetField("player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(kp, player);

            // Set UI references
            typeof(NavKeypad.KeypadPlayerInteraction)
                .GetField("interactionPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(kp, promptUI);

            typeof(NavKeypad.KeypadPlayerInteraction)
                .GetField("promptText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(kp, promptText);
        }

        Debug.Log($"Setup {keypads.Length} keypads");
    }
}