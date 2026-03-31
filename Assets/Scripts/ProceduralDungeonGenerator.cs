using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//
public partial class ProceduralDungeonGenerator : MonoBehaviour
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

        public bool HasLeftOrRightEdge()
        {
            return north == EdgeType.Left || north == EdgeType.Right ||
                   south == EdgeType.Left || south == EdgeType.Right ||
                   east == EdgeType.Left || east == EdgeType.Right ||
                   west == EdgeType.Left || west == EdgeType.Right;
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
    [Tooltip("Print a grid map + isolation report to the Console after each level generates.")]
    public bool debugPrintLayout = false;
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

    void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }

    void Start()
    {
        InitializeTileConfigs();
        if (generateOnStart) GenerateDungeon();
    }

    void InitializeTileConfigs()
    {
        tileConfigs = new Dictionary<string, TileConfig>();
        TileConfigData.Populate(tileConfigs);
        Debug.Log($"Initialized {tileConfigs.Count} tile configurations");
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

        // Move the player into level 0's spawn room now that generation and NavMesh are ready
        if (GameManager.Instance != null)
            GameManager.Instance.PlacePlayerAtSpawnRoom();
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

        // pathDir is lifted out so connStart can use it after the forced-tiles block.
        Vector2Int pathDir = Vector2Int.zero;

        // If starting from stairs landing, force a path inward from the edge
        if (levelIndex > 0 && stairsPositions.Count >= levelIndex)
        {
            // Determine which edge stairs are on and create path inward
            if (startX == 0) pathDir = new Vector2Int(1, 0); // West edge - go East
            else if (startX == dungeonWidth - 1) pathDir = new Vector2Int(-1, 0); // East edge - go West
            else if (startZ == 0) pathDir = new Vector2Int(0, 1); // South edge - go North
            else if (startZ == dungeonHeight - 1) pathDir = new Vector2Int(0, -1); // North edge - go South

            // Force-place 3 tiles leading inward from stairs.
            // i=0 (perimeter/stairs position): room tile whose INWARD edge (pathDir direction)
            //     is Open — this clears the wall at the bottom of the stairway exit.
            // i=1 (safe room entrance): requires Open toward stairs (so the player can walk in)
            //     AND forward Open toward dungeon (so BFS connects through, not into a pocket).
            // i=2 (first dungeon tile past safe room): TryPlaceCompatibleTile ensures its
            //     face toward i=1 matches, so opening a door doesn't reveal a wall.
            for (int i = 0; i < 3; i++)
            {
                int px = startX + (pathDir.x * i);
                int pz = startZ + (pathDir.y * i);

                if (IsInBounds(px, pz))
                {
                    if (i == 0)
                    {
                        // The stairway prefab placed by the level above already occupies
                        // this grid slot and acts as the floor tile here. Placing another
                        // tile on top would make the BFS use the generated tile's edge data
                        // instead of the stairway's, causing adjacent perimeter tiles to
                        // connect incorrectly and appear isolated.
                        // Mark visited so the BFS never fills this slot — no tile, no frontier.
                        visited.Add(new Vector2Int(px, pz));
                    }
                    else
                    {
                        if (i == 1)
                            PlaceStaircaseEntranceTile(px, pz, pathDir, levelParent);
                        else
                            TryPlaceCompatibleTile(px, pz, levelParent);

                        visited.Add(new Vector2Int(px, pz));
                        frontier.Enqueue(new Vector2Int(px, pz));
                        tilesPlaced++;
                    }
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

        // For level 0 the BFS starts from the grid centre — a well-connected hub.
        // For level 1+ startX/Z is the perimeter landing position (P0). Starting flood
        // fills from the perimeter gives a single-entry view that can under-count reachable
        // tiles, letting isolated pockets slip through repair undetected and causing code
        // numbers / safe-room doors to appear in disconnected rooms.
        // Use i=1 (one step inward, the guaranteed safe-room entrance tile) as the
        // connectivity anchor — it's interior, force-placed, and connected to both the
        // stairway and the rest of the dungeon.
        int connStartX = (levelIndex > 0 && pathDir != Vector2Int.zero)
            ? startX + pathDir.x : startX;
        int connStartZ = (levelIndex > 0 && pathDir != Vector2Int.zero)
            ? startZ + pathDir.y : startZ;

        // Run connectivity validation and repair with multiple passes
        Debug.Log($"Level {levelIndex}: Running connectivity validation (connStart:{connStartX},{connStartZ})...");
        List<Vector2Int> isolated = FindIsolatedTiles(connStartX, connStartZ);

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
                if (TryRepairIsolatedTile(isoPos.x, isoPos.y, connStartX, connStartZ, levelParent))
                {
                    repaired++;
                }
            }

            Debug.Log($"Level {levelIndex}: Pass {repairPass} repaired {repaired}/{isolated.Count} tiles");

            // Re-check connectivity after this pass
            isolated = FindIsolatedTiles(connStartX, connStartZ);

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
        if (!ValidateFullConnectivity(connStartX, connStartZ, levelParent, levelIndex))
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
        PlaceStairsOnEdge(levelParent, levelIndex, connStartX, connStartZ);

        // Set up safe room doors on the entrance tile (one step inward from the stairs)
        SafeRoomSetup safeRoom = FindObjectOfType<SafeRoomSetup>();
        if (safeRoom != null)
            safeRoom.SetupLevel(this, levelIndex, levelParent);

        // Set up the player spawn room on a different perimeter edge of this level
        SpawnRoomSetup spawnRoom = FindObjectOfType<SpawnRoomSetup>();
        if (spawnRoom != null)
            spawnRoom.SetupLevel(this, levelIndex, levelParent);

        SetupKeypads();

        // Notify CodeNumberManager so it can spawn collectible code numbers for this level
        CodeNumberManager codeManager = FindObjectOfType<CodeNumberManager>();
        if (codeManager != null)
            codeManager.InitializeForLevel(this, levelIndex, connStartX, connStartZ);

        // Spawn one computer terminal on a reachable floor tile for this level
        ComputerSpawner computerSpawner = FindObjectOfType<ComputerSpawner>();
        if (computerSpawner != null)
            computerSpawner.InitializeForLevel(this, levelIndex, connStartX, connStartZ);

        PrintLevelDebug(levelIndex, connStartX, connStartZ);

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

    // i=1 in the forced staircase path — this is the actual safe room entrance tile.
    // Two constraints beyond normal room tile:
    //   1. Edge facing BACK toward the stairs (opposite of pathDir) must be Open — player
    //      walks off the stairs straight into the safe room without hitting a wall.
    //   2. At least one OTHER edge must be Open — SafeRoomSetup needs somewhere to put doors.
    // Falls back progressively, relaxing constraint 2 then 1, before using TryPlaceCompatibleTile.
    void PlaceStaircaseEntranceTile(int x, int z, Vector2Int pathDir, GameObject parent)
    {
        // The "back" direction (toward stairs) is opposite to pathDir.
        // e.g. pathDir = (1,0) → moving east → back = west.

        List<GameObject> candidates = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            TileConfig cfg = tileConfigs[prefab.name];

            if (!cfg.IsRoomTile()) continue;

            // Back edge (toward stairs) must be Open
            if (pathDir.x ==  1 && cfg.west  != EdgeType.Open) continue;
            if (pathDir.x == -1 && cfg.east  != EdgeType.Open) continue;
            if (pathDir.y ==  1 && cfg.south != EdgeType.Open) continue;
            if (pathDir.y == -1 && cfg.north != EdgeType.Open) continue;

            // The FORWARD edge (same direction as pathDir, toward i=2 / main dungeon)
            // must also be Open. This guarantees a straight-through corridor
            // i=0 → i=1 → i=2 so the BFS always connects the safe-room pocket to
            // the rest of the dungeon. Without this, a corner tile (e.g. N+W only)
            // can be chosen, sending the BFS into an isolated corner pocket.
            if (pathDir.x ==  1 && cfg.east  != EdgeType.Open) continue;
            if (pathDir.x == -1 && cfg.west  != EdgeType.Open) continue;
            if (pathDir.y ==  1 && cfg.north != EdgeType.Open) continue;
            if (pathDir.y == -1 && cfg.south != EdgeType.Open) continue;

            candidates.Add(prefab);
        }

        if (candidates.Count > 0)
        {
            PlaceTile(x, z, candidates[Random.Range(0, candidates.Count)], parent);
            return;
        }

        // Fallback 1: relax the forward-Open constraint, just keep back edge Open + IsRoomTile
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            TileConfig cfg = tileConfigs[prefab.name];
            if (!cfg.IsRoomTile()) continue;
            if (pathDir.x ==  1 && cfg.west  != EdgeType.Open) continue;
            if (pathDir.x == -1 && cfg.east  != EdgeType.Open) continue;
            if (pathDir.y ==  1 && cfg.south != EdgeType.Open) continue;
            if (pathDir.y == -1 && cfg.north != EdgeType.Open) continue;
            PlaceTile(x, z, prefab, parent);
            return;
        }

        // Fallback 2: any compatible tile — back-edge-open not guaranteed but at least edge-matched
        TryPlaceCompatibleTile(x, z, parent);
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
#if UNITY_EDITOR
            Debug.Log($"Used relaxed matching at ({x},{z}) - {chosen.name}");
#endif
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
#if UNITY_EDITOR
                Debug.LogWarning($"Perimeter fallback at ({x},{z}) - {perimFallback.name}");
#endif
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
#if UNITY_EDITOR
                Debug.LogWarning($"Used fill tile at ({x},{z}) - marked as dead end");
#endif
                return false; // Return FALSE to prevent frontier expansion
            }
        }

        return false;
    }

    int CountSingleOpeningLRNeighbors(int x, int z)
    {
        int count = 0;
        Vector2Int[] dirs = {
            new Vector2Int(x - 1, z), new Vector2Int(x + 1, z),
            new Vector2Int(x, z + 1), new Vector2Int(x, z - 1)
        };
        foreach (Vector2Int d in dirs)
        {
            if (!IsInBounds(d.x, d.y) || placedConfigs[d.x, d.y] == null) continue;
            TileConfig neighbor = placedConfigs[d.x, d.y];
            if (neighbor.HasLeftOrRightEdge() && neighbor.GetOpeningCount() == 1)
                count++;
        }
        return count;
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
#if UNITY_EDITOR
                Debug.LogWarning($"Rejected Fill tile at ({x},{z}) - cannot place at perimeter (walk-off hazard)");
#endif
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
#if UNITY_EDITOR
                    Debug.Log($"Rejected {config.tileName} at ({x},{z}) - {openingCount} openings near edge");
#endif
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
#if UNITY_EDITOR
                Debug.Log($"Rejected {config.tileName} at ({x},{z}) - would form closed loop with {twoOpeningNeighbors} 2-opening neighbors");
#endif
                return false;
            }
        }

        // Prevent Left/Right tiles from forming isolated chains
        // THalls + TCorner combinations can create 3-tile isolated groups
        if (config.HasLeftOrRightEdge())
        {
            int singleOpeningLRNeighbors = CountSingleOpeningLRNeighbors(x, z);

            if (openingCount == 1 && singleOpeningLRNeighbors >= 1)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - L/R tiles clustering (would form isolated chain)");
#endif
                return false;
            }

            if (openingCount == 2 && singleOpeningLRNeighbors >= 2)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - would create L/R dead-end chain");
#endif
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
                    if (!adj.HasLeftOrRightEdge() || adj.GetOpeningCount() != 2) continue;

                    // This neighbor is a 2-opening L/R tile (THalls-style)
                    // Check if its OTHER neighbors already include a single-opening L/R tile
                    Vector2Int[] adjDirs = {
                        new Vector2Int(d.x - 1, d.y), new Vector2Int(d.x + 1, d.y),
                        new Vector2Int(d.x, d.y + 1), new Vector2Int(d.x, d.y - 1)
                    };
                    foreach (Vector2Int ad in adjDirs)
                    {
                        if (ad.x == x && ad.y == z) continue;
                        if (!IsInBounds(ad.x, ad.y) || placedConfigs[ad.x, ad.y] == null) continue;
                        TileConfig farNeighbor = placedConfigs[ad.x, ad.y];
                        if (farNeighbor.HasLeftOrRightEdge() && farNeighbor.GetOpeningCount() == 1)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - would seal L/R chain through ({d.x},{d.y})");
#endif
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

    void PlaceStairsOnEdge(GameObject parent, int levelIndex, int connStartX, int connStartZ)
    {
        // Find all stairs tiles
        List<GameObject> stairsPrefabs = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab != null && prefab.name.Contains("Stairs"))
                stairsPrefabs.Add(prefab);
        }

        if (stairsPrefabs.Count == 0) return;

        // Build reachable set from connStart so we only place stairs where the
        // player can actually reach them, and where the one-step-inward tile is
        // a proper room tile (no corridor Left/Right/Center edges that would
        // create a "middle opening" gap in the safe-room walls).
        HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
        {
            var q = new Queue<Vector2Int>();
            var start = new Vector2Int(connStartX, connStartZ);
            if (IsInBounds(connStartX, connStartZ) && placedTiles[connStartX, connStartZ] != null)
            {
                q.Enqueue(start);
                reachable.Add(start);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    Vector2Int[] ns = {
                        new Vector2Int(cur.x-1, cur.y), new Vector2Int(cur.x+1, cur.y),
                        new Vector2Int(cur.x, cur.y+1), new Vector2Int(cur.x, cur.y-1)
                    };
                    foreach (var n in ns)
                    {
                        if (!IsInBounds(n.x, n.y) || reachable.Contains(n)) continue;
                        if (placedTiles[n.x, n.y] == null) continue;
                        if (CanWalkBetween(cur.x, cur.y, n.x, n.y)) { reachable.Add(n); q.Enqueue(n); }
                    }
                }
            }
        }

        // Collect all valid (position, stairs_tile) pairs on perimeter.
        // Primary: inward tile must be reachable AND be a proper room tile.
        // Fallback: inward tile must be reachable (any tile type), used only when
        //           no room-tile positions exist so stairs always get placed.
        List<(int x, int z, GameObject prefab)> validPlacements    = new List<(int, int, GameObject)>();
        List<(int x, int z, GameObject prefab)> fallbackPlacements  = new List<(int, int, GameObject)>();

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                // Only check perimeter positions
                if (x != 0 && x != dungeonWidth - 1 && z != 0 && z != dungeonHeight - 1) continue;
                if (placedTiles[x, z] == null) continue;

                // The one-step-inward tile must be reachable from connStart AND
                // must be a room tile (all Open/Wall edges) so the safe room that
                // SafeRoomSetup wraps around it never has a Left/Right/Center gap.
                Vector2Int inward = DebugEntranceTile(new Vector2Int(x, z));
                if (!IsInBounds(inward.x, inward.y)) continue;
                if (!reachable.Contains(inward)) continue;
                TileConfig inwardCfg = placedConfigs[inward.x, inward.y];
                if (inwardCfg == null) continue;

                // On level 1+, connStart IS the spawn room (arrival tile).
                // Keep departure stairs at least 4 tiles (Manhattan) away so the
                // departure safe room never overlaps or sits adjacent to the spawn room.
                if (levelIndex > 0)
                {
                    int dist = Mathf.Abs(inward.x - connStartX) + Mathf.Abs(inward.y - connStartZ);
                    if (dist < 4) continue;
                }

                // Try each stairs tile
                foreach (GameObject stairsPrefab in stairsPrefabs)
                {
                    if (!tileConfigs.ContainsKey(stairsPrefab.name)) continue;
                    TileConfig stairsConfig = tileConfigs[stairsPrefab.name];

                    if (IsCompatibleWithNeighbors(x, z, stairsConfig, strict: false))
                    {
                        if (inwardCfg.IsRoomTile())
                            validPlacements.Add((x, z, stairsPrefab));
                        else
                            fallbackPlacements.Add((x, z, stairsPrefab));
                    }
                }
            }
        }

        // Use fallback only when no preferred positions exist.
        if (validPlacements.Count == 0 && fallbackPlacements.Count > 0)
        {
            validPlacements = fallbackPlacements;
            Debug.LogWarning($"Level {levelIndex}: No room-tile inward position found for stairs — using fallback.");
        }

        // Pick one random valid placement
        if (validPlacements.Count > 0)
        {
            var chosen = validPlacements[Random.Range(0, validPlacements.Count)];

            // Remove old tile
            SafeDestroy(placedTiles[chosen.x, chosen.z]);

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

    [ContextMenu("Clear Dungeon")]
    public void ClearDungeon()
    {
        // Clear NPC spawns first
        DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
        if (navSetup != null)
            navSetup.ClearAllSpawns();

        // Clear safe room doors
        SafeRoomSetup safeRoom = FindObjectOfType<SafeRoomSetup>();
        if (safeRoom != null)
            safeRoom.ClearAll();

        // Clear spawn room doors and spawn points
        SpawnRoomSetup spawnRoom = FindObjectOfType<SpawnRoomSetup>();
        if (spawnRoom != null)
            spawnRoom.ClearAll();

        // Clear all code numbers across all levels
        CodeNumberManager codeNumbers = FindObjectOfType<CodeNumberManager>();
        if (codeNumbers != null)
            codeNumbers.ClearAll();

        // Clear all spawned computers across all levels
        ComputerSpawner computerSpawner = FindObjectOfType<ComputerSpawner>();
        if (computerSpawner != null)
            computerSpawner.ClearAll();

        // Clear all level parents and their children
        if (levelParents != null)
        {
            foreach (GameObject levelParent in levelParents)
            {
                SafeDestroy(levelParent);
            }
            levelParents.Clear();
        }

        // Clear old single-level tiles (for backwards compatibility)
        if (placedTiles != null)
            for (int x = 0; x < placedTiles.GetLength(0); x++)
                for (int z = 0; z < placedTiles.GetLength(1); z++)
                    SafeDestroy(placedTiles[x, z]);

        if (decorationObjects != null)
        {
            foreach (GameObject decoration in decorationObjects)
                SafeDestroy(decoration);
            decorationObjects.Clear();
        }

        // Clear all multi-level data
        allLevelConfigs.Clear();
        stairsPositions.Clear();

        placedTiles = null;
        placedConfigs = null;
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

            // Wire the onAccessGranted event to the nearest SlidingDoor so it actually opens.
            // The keypad and its door are on the same stairs prefab, so we search within
            // the parent hierarchy first, then fall back to the nearest door in the scene.
            NavKeypad.Keypad keypadLogic = kp.GetComponentInParent<NavKeypad.Keypad>();
            if (keypadLogic == null) keypadLogic = kp.GetComponent<NavKeypad.Keypad>();

            if (keypadLogic != null)
            {
                NavKeypad.SlidingDoor door = kp.GetComponentInParent<NavKeypad.SlidingDoor>();
                if (door == null) door = kp.GetComponentInChildren<NavKeypad.SlidingDoor>();

                if (door != null)
                {
                    // Remove any stale listeners from a previous generation, then add fresh.
                    keypadLogic.OnAccessGranted.RemoveAllListeners();
                    keypadLogic.OnAccessGranted.AddListener(door.OpenDoor);
                    Debug.Log($"SetupKeypads: Wired {keypadLogic.name} → {door.name}.OpenDoor()");
                }
                else
                {
                    Debug.LogWarning($"SetupKeypads: No SlidingDoor found near keypad {kp.name}. Check the stairs prefab hierarchy.");
                }
            }
        }

        Debug.Log($"Setup {keypads.Length} keypads");
    }
}