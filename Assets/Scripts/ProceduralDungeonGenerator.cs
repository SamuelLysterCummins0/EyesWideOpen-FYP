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

    [Header("Generation Settings")]
    public int dungeonWidth = 12;
    public int dungeonHeight = 12;
    public float tileSize = 4f;
    public int targetTileCount = 50;

    [Header("Tile Preferences")]
    [Range(0f, 1f)] public float roomTileProbability = 0.65f;

    [Header("Repair Settings")]
    [Tooltip("If more tiles than this stay isolated after repair passes, regenerate the level")]
    public int isolatedTileThreshold = 10;
    [Tooltip("How many times to retry a level before accepting the best attempt")]
    public int maxGenerationAttempts = 8;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    [Tooltip("Print a grid map and isolation report after each level generates")]
    public bool debugPrintLayout = false;
    public bool generateOnStart = false;

    [Header("Multi-Level Settings")]
    public int numberOfLevels = 2;
    public float levelHeight = 4f;

    private List<GameObject> levelParents = new List<GameObject>();

    private bool deferNavMesh = false;
    private List<(GameObject parent, int index, TileConfig[,] configs, int connStartX, int connStartZ)> pendingNavMeshLevels
        = new List<(GameObject, int, TileConfig[,], int, int)>();

    private Dictionary<string, TileConfig> tileConfigs;
    private GameObject[,] placedTiles;
    private TileConfig[,] placedConfigs;
    private bool isRepairing = false;
    private Vector2Int safeRoomEntrancePos = new Vector2Int(-1, -1);
    private DungeonLevelVisibility levelVisibility;

    private int         bestAttemptIsolatedCount;
    private GameObject  bestAttemptLevelParent;
    private GameObject[,] bestAttemptPlacedTiles;
    private TileConfig[,]  bestAttemptPlacedConfigs;
    private List<Vector2Int> stairsPositions = new List<Vector2Int>();
    private Dictionary<int, TileConfig[,]> allLevelConfigs = new Dictionary<int, TileConfig[,]>();
    private Dictionary<int, HashSet<Vector2Int>> allLevelReachable = new Dictionary<int, HashSet<Vector2Int>>();
    public HashSet<Vector2Int> GetLevelReachable(int levelIndex)
        => allLevelReachable.TryGetValue(levelIndex, out var set) ? set : new HashSet<Vector2Int>();

    public void RemoveTile(int x, int z)
    {
        if (!IsInBounds(x, z)) return;
        SafeDestroy(placedTiles[x, z]);
        placedTiles[x, z]  = null;
        placedConfigs[x, z] = null;
    }

    public void PlaceFillTileAt(int x, int z, GameObject parent)
    {
        if (!IsInBounds(x, z)) return;

        GameObject fillPrefab = System.Array.Find(
            allTilePrefabs, p => p != null && p.name == "Tiles_01_Fill");

        if (fillPrefab == null)
        {
            Debug.LogWarning("[ProceduralDungeonGenerator] Tiles_01_Fill prefab not found — cannot place detonation fill.");
            return;
        }

        RemoveTile(x, z);
        PlaceTile(x, z, fillPrefab, parent);
    }

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

        levelVisibility = GetComponent<DungeonLevelVisibility>();

        if (Application.isPlaying)
        {
            var sgm = SaveGameManager.Instance;
            if (sgm != null && sgm.HasPendingLoad())
            {
                SaveData sd = sgm.GetCurrentSaveData();
                if (sd != null && sd.dungeonSeed != 0)
                {
                    Random.InitState(sd.dungeonSeed);
                    Debug.Log($"[ProceduralDungeonGenerator] Loading save — seeding RNG with {sd.dungeonSeed}");
                }
            }
            else
            {
                int newSeed = Mathf.Abs(System.Environment.TickCount);
                Random.InitState(newSeed);
                sgm?.RecordDungeonSeed(newSeed);
                Debug.Log($"[ProceduralDungeonGenerator] New game — RNG seed {newSeed}");
            }
        }

        ClearDungeon();
        levelParents.Clear();
        stairsPositions.Clear();
        allLevelConfigs.Clear();
        pendingNavMeshLevels.Clear();

        if (Application.isPlaying)
        {
            StartCoroutine(GenerateDungeonCoroutine());
        }
        else
        {
            deferNavMesh = false;
            for (int level = 0; level < numberOfLevels; level++)
                GenerateLevel(level);

            levelVisibility?.InitialHide(levelHeight, dungeonWidth, stairsPositions, levelParents, tileSize);
        }
    }

    private IEnumerator GenerateDungeonCoroutine()
    {
        deferNavMesh = true;
        for (int level = 0; level < numberOfLevels; level++)
            GenerateLevel(level);
        deferNavMesh = false;

        yield return null; // wait a frame so MeshRenderers are registered before NavMesh bake

        DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
        if (navSetup != null)
        {
            foreach (var entry in pendingNavMeshLevels)
                navSetup.SetupLevel(entry.parent, entry.index, entry.configs,
                                    dungeonWidth, dungeonHeight, tileSize, levelHeight,
                                    entry.connStartX, entry.connStartZ);
        }
        pendingNavMeshLevels.Clear();

        levelVisibility?.InitialHide(levelHeight, dungeonWidth, stairsPositions, levelParents, tileSize);

        SpawnRoomSetup introSpawnRef = FindObjectOfType<SpawnRoomSetup>();
        IntroRoomSetup introRoom = FindObjectOfType<IntroRoomSetup>();
        if (introRoom != null && introSpawnRef != null)
            introRoom.PlaceIntroRoom(introSpawnRef);

        if (GameManager.Instance != null)
            GameManager.Instance.PlacePlayerAtSpawnRoom();
    }

    void GenerateLevel(int levelIndex, int attemptNumber = 1)
    {
        GameObject levelParent = new GameObject($"Level_{levelIndex}");
        levelParent.transform.parent = transform;
        levelParent.transform.position = new Vector3(0, levelIndex * -levelHeight, 0);
        while (levelParents.Count <= levelIndex)
            levelParents.Add(null);
        levelParents[levelIndex] = levelParent;

        placedTiles = new GameObject[dungeonWidth, dungeonHeight];
        placedConfigs = new TileConfig[dungeonWidth, dungeonHeight];

        int startX, startZ;
        if (levelIndex > 0 && stairsPositions.Count >= levelIndex)
        {
            Vector2Int landingPos = stairsPositions[levelIndex - 1];
            startX = landingPos.x;
            startZ = landingPos.y;
        }
        else
        {
            startX = dungeonWidth / 2;
            startZ = dungeonHeight / 2;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        int tilesPlaced = 0;

        Vector2Int pathDir = Vector2Int.zero;

        if (levelIndex > 0 && stairsPositions.Count >= levelIndex)
        {
            if (startX == 0) pathDir = new Vector2Int(1, 0);
            else if (startX == dungeonWidth - 1) pathDir = new Vector2Int(-1, 0);
            else if (startZ == 0) pathDir = new Vector2Int(0, 1);
            else if (startZ == dungeonHeight - 1) pathDir = new Vector2Int(0, -1);

            for (int i = 0; i < 5; i++)
            {
                int px = startX + (pathDir.x * i);
                int pz = startZ + (pathDir.y * i);

                if (IsInBounds(px, pz))
                {
                    if (i == 0)
                    {
                        // Stairway prefab from the level above occupies this slot already — don't overwrite it.
                        visited.Add(new Vector2Int(px, pz));
                    }
                    else
                    {
                        if (i == 1)
                        {
                            PlaceStaircaseEntranceTile(px, pz, pathDir, levelParent);
                            safeRoomEntrancePos = new Vector2Int(px, pz);
                        }
                        else
                            TryPlaceCompatibleTile(px, pz, levelParent);

                        visited.Add(new Vector2Int(px, pz));
                        frontier.Enqueue(new Vector2Int(px, pz));
                        tilesPlaced++;
                    }
                }
            }

        }
        else
        {
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

        safeRoomEntrancePos = new Vector2Int(-1, -1);

        // Use the tile one step inward from the stairs as the connectivity anchor (level 1+),
        // since starting flood fill from the perimeter edge under-counts reachable tiles.
        int connStartX = (levelIndex > 0 && pathDir != Vector2Int.zero)
            ? startX + pathDir.x : startX;
        int connStartZ = (levelIndex > 0 && pathDir != Vector2Int.zero)
            ? startZ + pathDir.y : startZ;

        List<Vector2Int> isolated = FindIsolatedTiles(connStartX, connStartZ);

        int repairPass = 0;
        int maxRepairPasses = 7;

        isRepairing = true;
        while (isolated.Count > 0 && repairPass < maxRepairPasses)
        {
            repairPass++;
            int repaired = 0;
            foreach (Vector2Int isoPos in isolated)
            {
                if (TryRepairIsolatedTile(isoPos.x, isoPos.y, connStartX, connStartZ, levelParent))
                {
                    repaired++;
                }
            }

            isolated = FindIsolatedTiles(connStartX, connStartZ);

            if (isolated.Count == 0) break;
            if (repaired == 0) break;
        }
        isRepairing = false;

        if (isolated.Count > isolatedTileThreshold)
        {
            isRepairing = true;
            int extraPass = 0;
            while (isolated.Count > isolatedTileThreshold && extraPass < 5)
            {
                extraPass++;
                int repaired = 0;
                foreach (Vector2Int isoPos in isolated)
                {
                    if (TryRepairIsolatedTile(isoPos.x, isoPos.y, connStartX, connStartZ, levelParent))
                        repaired++;
                }
                isolated = FindIsolatedTiles(connStartX, connStartZ);
                if (repaired == 0) break;
            }
            isRepairing = false;
        }

        if (attemptNumber == 1)
        {
            bestAttemptIsolatedCount = int.MaxValue;
            bestAttemptLevelParent   = null;
            bestAttemptPlacedTiles   = null;
            bestAttemptPlacedConfigs = null;
        }
        if (isolated.Count < bestAttemptIsolatedCount)
        {
            if (bestAttemptLevelParent != null && bestAttemptLevelParent != levelParent)
                DestroyImmediate(bestAttemptLevelParent);
            bestAttemptLevelParent   = levelParent;
            bestAttemptIsolatedCount = isolated.Count;
            bestAttemptPlacedTiles   = (GameObject[,])placedTiles.Clone();
            bestAttemptPlacedConfigs = (TileConfig[,])placedConfigs.Clone();
        }

        // Run comprehensive validation
        if (!ValidateFullConnectivity(connStartX, connStartZ, levelParent, levelIndex))
        {
            if (attemptNumber < maxGenerationAttempts)
            {
                Debug.LogWarning($"Level {levelIndex} attempt {attemptNumber} failed validation. Retrying... ({attemptNumber}/{maxGenerationAttempts})");

                // Only destroy this attempt if it is NOT the saved best — we want to keep
                // the best parent alive across retries so we can fall back to it at the end.
                if (levelParent != bestAttemptLevelParent && levelParent != null)
                    DestroyImmediate(levelParent);
                if (levelParents.Count > levelIndex)
                    levelParents[levelIndex] = null;

                GenerateLevel(levelIndex, attemptNumber + 1);
                return;
            }
            else
            {
                // All attempts exhausted. If a previous attempt had fewer isolated tiles,
                // switch to it now so the rest of GenerateLevel (decorations, stairs, etc.)
                // runs on the best data rather than whatever the last attempt produced.
                if (bestAttemptLevelParent != null && bestAttemptLevelParent != levelParent)
                {
                    DestroyImmediate(levelParent);
                    levelParent   = bestAttemptLevelParent;
                    placedTiles   = bestAttemptPlacedTiles;
                    placedConfigs = bestAttemptPlacedConfigs;
                    if (levelParents.Count > levelIndex) levelParents[levelIndex] = levelParent;
                    Debug.LogWarning($"Level {levelIndex}: all {maxGenerationAttempts} attempts exceeded threshold — " +
                                     $"using best result ({bestAttemptIsolatedCount} isolated tiles).");
                }
                else
                {
                    Debug.LogWarning($"Level {levelIndex}: all {maxGenerationAttempts} attempts exceeded threshold — " +
                                     $"using current result ({isolated.Count} isolated tiles).");
                }

                // Null out isolated tiles from the tracking arrays so nothing spawns there,
                // but keep the GameObjects alive so there's no visible hole in the grid.
                List<Vector2Int> finalIsolated = FindIsolatedTiles(connStartX, connStartZ);
                if (finalIsolated.Count > 0)
                {
                    foreach (Vector2Int isoPos in finalIsolated)
                    {
                        GameObject orphan = placedTiles[isoPos.x, isoPos.y];
                        if (orphan != null)
                            orphan.name = $"Orphan_{isoPos.x}_{isoPos.y}_Isolated";
                        placedTiles[isoPos.x, isoPos.y]   = null;
                        placedConfigs[isoPos.x, isoPos.y] = null;
                    }
                    Debug.LogWarning($"[ProceduralDungeonGenerator] Level {levelIndex}: orphaned {finalIsolated.Count} unreachable isolated tile(s) (kept visible, excluded from gameplay) after all {maxGenerationAttempts} attempts failed to meet threshold ({isolatedTileThreshold}).");
                }

                // Tracking fields are cleared in the unconditional block below.
            }
        }

        if (bestAttemptLevelParent != null && bestAttemptLevelParent != levelParent)
            DestroyImmediate(bestAttemptLevelParent);
        bestAttemptLevelParent   = null;
        bestAttemptPlacedTiles   = null;
        bestAttemptPlacedConfigs = null;
        bestAttemptIsolatedCount = int.MaxValue;

        // Only place departure stairs if there is a level below this one
        if (levelIndex < numberOfLevels - 1)
            PlaceStairsOnEdge(levelParent, levelIndex, connStartX, connStartZ);

        // Set up safe room doors on the entrance tile (one step inward from the stairs)
        SafeRoomSetup safeRoom = FindObjectOfType<SafeRoomSetup>();
        if (safeRoom != null)
            safeRoom.SetupLevel(this, levelIndex, levelParent);

        // Set up the player spawn room on a different perimeter edge of this level
        SpawnRoomSetup spawnRoom = FindObjectOfType<SpawnRoomSetup>();
        if (spawnRoom != null)
            spawnRoom.SetupLevel(this, levelIndex, levelParent, connStartX, connStartZ);

        SetupKeypads();

        int wallNumberCount = levelIndex == 0 ? 4 : (levelIndex == 1 ? 3 : 2);
        bool spawnHiddenRoom = levelIndex >= 2;
        bool spawnComputer   = levelIndex >= 1;

        ComputerRoomSetup computerRoom = FindObjectOfType<ComputerRoomSetup>();
        if (computerRoom != null && spawnComputer)
            computerRoom.SetupLevel(this, levelIndex, levelParent, spawnRoom, connStartX, connStartZ);

        HiddenRoomSetup hiddenRoom = HiddenRoomSetup.Instance;
        if (hiddenRoom == null) hiddenRoom = FindObjectOfType<HiddenRoomSetup>();
        if (hiddenRoom != null && spawnHiddenRoom)
            hiddenRoom.SetupLevel(this, levelIndex, levelParent, spawnRoom, connStartX, connStartZ);

        if (levelIndex == numberOfLevels - 1)
        {
            DetonationRoomSetup detRoom = DetonationRoomSetup.Instance;
            if (detRoom == null) detRoom = FindObjectOfType<DetonationRoomSetup>();
            if (detRoom != null)
                detRoom.SetupLevel(this, levelIndex, levelParent,
                                   spawnRoom, computerRoom, hiddenRoom,
                                   connStartX, connStartZ);
        }

        CodeNumberManager codeManager = CodeNumberManager.Instance;
        if (codeManager == null) codeManager = FindObjectOfType<CodeNumberManager>();
        if (codeManager != null)
            codeManager.InitializeForLevel(this, levelIndex, connStartX, connStartZ, wallNumberCount);

        LockerSetup lockerSetup = FindObjectOfType<LockerSetup>();
        if (lockerSetup != null)
        {
            NPCSpawnManager npcSpawnMgr = FindObjectOfType<NPCSpawnManager>();
            lockerSetup.SetupLevel(this, levelIndex, levelParent,
                                   spawnRoom, hiddenRoom, computerRoom, safeRoom, npcSpawnMgr);
        }

        BatterySpawnSetup batterySpawn = FindObjectOfType<BatterySpawnSetup>();
        if (batterySpawn != null)
            batterySpawn.SetupLevel(this, levelIndex, levelParent,
                                    spawnRoom, safeRoom, hiddenRoom, computerRoom);

        // Now that CodeNumberManager has generated digits, spawn the hidden room's number (slot 2)
        if (hiddenRoom != null && spawnHiddenRoom)
            hiddenRoom.SpawnNumberAfterInit(levelIndex, tileSize);

        PrintLevelDebug(levelIndex, connStartX, connStartZ);

        TileConfig[,] levelConfigsCopy = new TileConfig[dungeonWidth, dungeonHeight];
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
                levelConfigsCopy[x, z] = placedConfigs[x, z];
        allLevelConfigs[levelIndex] = levelConfigsCopy;
        allLevelReachable[levelIndex] = FloodFillReachable(connStartX, connStartZ);

        if (!deferNavMesh)
        {
            DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
            if (navSetup != null)
                navSetup.SetupLevel(levelParent, levelIndex, levelConfigsCopy,
                                    dungeonWidth, dungeonHeight, tileSize, levelHeight,
                                    connStartX, connStartZ);
            else
                Debug.LogError("DungeonNavMeshSetup component NOT FOUND!");
        }
        else
        {
            pendingNavMeshLevels.Add((levelParent, levelIndex, levelConfigsCopy, connStartX, connStartZ));
        }

        DungeonWallSealer sealer = FindObjectOfType<DungeonWallSealer>();
        if (sealer != null)
            sealer.SealLevel(this, levelIndex, levelParent);

        levelVisibility?.RegisterLevel(levelIndex, levelParent);
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

    void PlaceStaircaseEntranceTile(int x, int z, Vector2Int pathDir, GameObject parent)
    {
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

            // Forward edge must also be Open to guarantee the BFS connects through into the dungeon.
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

        // Fallback: relax the forward-Open constraint
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

        TryPlaceCompatibleTile(x, z, parent);
    }

    bool TryPlaceCompatibleTile(int x, int z, GameObject parent)
    {
        List<GameObject> strictCompatibleTiles = new List<GameObject>();
        List<GameObject> strictRoomTiles = new List<GameObject>();

        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            TileConfig config = tileConfigs[prefab.name];
            if (IsCompatibleWithNeighbors(x, z, config, strict: true))
            {
                if (config.IsRoomTile()) strictRoomTiles.Add(prefab);
                else strictCompatibleTiles.Add(prefab);
            }
        }

        GameObject chosen = null;

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

        List<GameObject> relaxedCompatibleTiles = new List<GameObject>();
        List<GameObject> relaxedRoomTiles = new List<GameObject>();

        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
            if (prefab.name.Contains("Stairs")) continue;
            TileConfig config = tileConfigs[prefab.name];
            if (IsCompatibleWithNeighbors(x, z, config, strict: false))
            {
                if (config.IsRoomTile()) relaxedRoomTiles.Add(prefab);
                else relaxedCompatibleTiles.Add(prefab);
            }
        }

        if (relaxedRoomTiles.Count > 0 && Random.value < roomTileProbability)
            chosen = relaxedRoomTiles[Random.Range(0, relaxedRoomTiles.Count)];
        else if (relaxedCompatibleTiles.Count > 0)
            chosen = relaxedCompatibleTiles[Random.Range(0, relaxedCompatibleTiles.Count)];
        else if (relaxedRoomTiles.Count > 0)
            chosen = relaxedRoomTiles[Random.Range(0, relaxedRoomTiles.Count)];

        if (chosen != null)
        {
            PlaceTile(x, z, chosen, parent);
            return true;
        }

        bool isPerimeter = (x == 0 || x == dungeonWidth - 1 || z == 0 || z == dungeonHeight - 1);

        if (isPerimeter)
        {
            List<GameObject> perimeterSafe = new List<GameObject>();
            foreach (GameObject prefab in allTilePrefabs)
            {
                if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
                if (prefab.name.Contains("Stairs") || prefab.name == "Tiles_01_Fill") continue;
                TileConfig cfg = tileConfigs[prefab.name];

                if (x == 0              && cfg.west  != EdgeType.Wall) continue;
                if (x == dungeonWidth-1 && cfg.east  != EdgeType.Wall) continue;
                if (z == 0              && cfg.south != EdgeType.Wall) continue;
                if (z == dungeonHeight-1 && cfg.north != EdgeType.Wall) continue;

                // Must have at least one inward-open edge so the tile isn't sealed from the dungeon.
                bool hasInwardOpen = false;
                if (x != 0              && cfg.west  == EdgeType.Open) hasInwardOpen = true;
                if (x != dungeonWidth-1 && cfg.east  == EdgeType.Open) hasInwardOpen = true;
                if (z != 0              && cfg.south == EdgeType.Open) hasInwardOpen = true;
                if (z != dungeonHeight-1 && cfg.north == EdgeType.Open) hasInwardOpen = true;
                if (!hasInwardOpen) continue;

                perimeterSafe.Add(prefab);
            }

            if (perimeterSafe.Count > 0)
            {
                GameObject perimFallback = perimeterSafe[Random.Range(0, perimeterSafe.Count)];
                PlaceTile(x, z, perimFallback, parent);
                return false; // Don't expand frontier from fallback placement
            }

            // Last resort: accept a fully-sealed perimeter tile to avoid a visible hole in the grid.
            List<GameObject> perimeterLastResort = new List<GameObject>();
            foreach (GameObject prefab in allTilePrefabs)
            {
                if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
                if (prefab.name.Contains("Stairs") || prefab.name == "Tiles_01_Fill") continue;
                TileConfig cfg = tileConfigs[prefab.name];

                if (x == 0              && cfg.west  != EdgeType.Wall) continue;
                if (x == dungeonWidth-1 && cfg.east  != EdgeType.Wall) continue;
                if (z == 0              && cfg.south != EdgeType.Wall) continue;
                if (z == dungeonHeight-1 && cfg.north != EdgeType.Wall) continue;

                perimeterLastResort.Add(prefab);
            }

            if (perimeterLastResort.Count > 0)
            {
                GameObject sealedFallback = perimeterLastResort[Random.Range(0, perimeterLastResort.Count)];
                PlaceTile(x, z, sealedFallback, parent);
                Debug.LogWarning($"[ProceduralDungeonGenerator] Perimeter cell ({x},{z}) used sealed-tile last resort — no inward-open candidate existed. Will be orphaned if isolated, but the cell is visually complete.");
                return false;
            }
        }

        if (!isPerimeter)
        {
            GameObject fillTile = System.Array.Find(allTilePrefabs, p => p != null && p.name == "Tiles_01_Fill");
            if (fillTile != null)
            {
                PlaceTile(x, z, fillTile, parent);
                return false;
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
        // Fill tile has no physical walls — can't go on the perimeter.
        if (config.tileName == "Tiles_01_Fill")
        {
            bool isPerimeter = (x == 0 || x == dungeonWidth - 1 ||
                                z == 0 || z == dungeonHeight - 1);
            if (isPerimeter)
            {
                return false;
            }
        }

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
                    return false;
                }
            }
        }

        if (config.GetOpeningCount() == 1)
        {
            if (x > 0 && placedConfigs[x - 1, z] != null && placedConfigs[x - 1, z].GetOpeningCount() == 1) return false;
            if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && placedConfigs[x + 1, z].GetOpeningCount() == 1) return false;
            if (z > 0 && placedConfigs[x, z - 1] != null && placedConfigs[x, z - 1].GetOpeningCount() == 1) return false;
            if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && placedConfigs[x, z + 1].GetOpeningCount() == 1) return false;
        }

        if (config.GetOpeningCount() == 2)
        {
            int twoOpeningNeighbors = 0;

            if (x > 0 && placedConfigs[x - 1, z] != null && placedConfigs[x - 1, z].GetOpeningCount() == 2)
                twoOpeningNeighbors++;
            if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && placedConfigs[x + 1, z].GetOpeningCount() == 2)
                twoOpeningNeighbors++;
            if (z > 0 && placedConfigs[x, z - 1] != null && placedConfigs[x, z - 1].GetOpeningCount() == 2)
                twoOpeningNeighbors++;
            if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && placedConfigs[x, z + 1].GetOpeningCount() == 2)
                twoOpeningNeighbors++;

            if (twoOpeningNeighbors >= 2)
                return false;
        }

        if (config.HasLeftOrRightEdge())
        {
            int singleOpeningLRNeighbors = CountSingleOpeningLRNeighbors(x, z);

            if (openingCount == 1 && singleOpeningLRNeighbors >= 1)
            {
                return false;
            }

            if (openingCount == 2 && singleOpeningLRNeighbors >= 2)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"Rejected {config.tileName} at ({x},{z}) - would create L/R dead-end chain");
#endif
                return false;
            }

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
                            return false;
                        }
                    }
                }
            }
        }

        // Tiles adjacent to the safe-room entrance can't have Left/Right edges facing into it —
        // those partial-wall gaps can't be doored and would let the player walk straight in.
        if (safeRoomEntrancePos.x >= 0)
        {
            int ex = safeRoomEntrancePos.x, ez = safeRoomEntrancePos.y;
            // Tile is west-neighbour of entrance → its east face must not be Left/Right
            if (x == ex - 1 && z == ez && (config.east == EdgeType.Left || config.east == EdgeType.Right)) return false;
            // Tile is east-neighbour of entrance → its west face must not be Left/Right
            if (x == ex + 1 && z == ez && (config.west == EdgeType.Left || config.west == EdgeType.Right)) return false;
            // Tile is south-neighbour of entrance → its north face must not be Left/Right
            if (x == ex && z == ez - 1 && (config.north == EdgeType.Left || config.north == EdgeType.Right)) return false;
            // Tile is north-neighbour of entrance → its south face must not be Left/Right
            if (x == ex && z == ez + 1 && (config.south == EdgeType.Left || config.south == EdgeType.Right)) return false;
        }

        if (x > 0 && placedConfigs[x - 1, z] != null && !EdgesMatch(config.west, placedConfigs[x - 1, z].east, strict)) return false;
        if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null && !EdgesMatch(config.east, placedConfigs[x + 1, z].west, strict)) return false;
        if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null && !EdgesMatch(config.north, placedConfigs[x, z + 1].south, strict)) return false;
        if (z > 0 && placedConfigs[x, z - 1] != null && !EdgesMatch(config.south, placedConfigs[x, z - 1].north, strict)) return false;
        return true;
    }

    bool EdgesMatch(EdgeType mine, EdgeType theirs, bool strict = true)
    {
        if (strict)
        {
            if (mine == EdgeType.Wall && theirs == EdgeType.Wall) return true;
            if (mine == EdgeType.Wall || theirs == EdgeType.Wall) return false;
            if (mine == EdgeType.Open || theirs == EdgeType.Open) return mine == EdgeType.Open && theirs == EdgeType.Open;
            if (mine == EdgeType.Center && theirs == EdgeType.Center) return true;
            if (mine == EdgeType.Left && theirs == EdgeType.Right) return true;
            if (mine == EdgeType.Right && theirs == EdgeType.Left) return true;
            return false;
        }
        else
        {
            if (mine == EdgeType.Wall && theirs == EdgeType.Wall) return true;
            if ((mine == EdgeType.Wall && theirs == EdgeType.Open) || (mine == EdgeType.Open && theirs == EdgeType.Wall)) return true;
            if (mine == EdgeType.Wall || theirs == EdgeType.Wall) return false;
            if (mine == EdgeType.Open || theirs == EdgeType.Open) return true;
            if (mine == EdgeType.Center && theirs == EdgeType.Center) return true;
            if (mine == EdgeType.Left && theirs == EdgeType.Right) return true;
            if (mine == EdgeType.Right && theirs == EdgeType.Left) return true;
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

    EdgeType GetReciprocal(TileConfig adj, string faceFromNeighbour)
    {
        if (adj.tileName == "Tiles_01_Fill") return EdgeType.Open;
        return faceFromNeighbour == "south" ? adj.south :
               faceFromNeighbour == "north" ? adj.north :
               faceFromNeighbour == "west"  ? adj.west  : adj.east;
    }

    bool IsValidSafeRoomEntrance(Vector2Int pos, TileConfig cfg, string skipDir)
    {
        bool hasDoor = false;
        bool CheckSide(EdgeType myEdge, int nx, int nz, string faceFromNeighbour)
        {
            if (myEdge != EdgeType.Open) return true; // Wall on this side — fine
            if (!IsInBounds(nx, nz)) return true;     // off-map — treated as sealed
            TileConfig adj = placedConfigs[nx, nz];
            if (adj == null) return true;
            EdgeType rec = GetReciprocal(adj, faceFromNeighbour);
            if (rec == EdgeType.Left || rec == EdgeType.Right) return false; // unblockable gap
            if (rec != EdgeType.Wall) hasDoor = true; // Open/Center → door can go here
            return true;
        }

        if (skipDir != "north" && !CheckSide(cfg.north, pos.x, pos.y + 1, "south")) return false;
        if (skipDir != "south" && !CheckSide(cfg.south, pos.x, pos.y - 1, "north")) return false;
        if (skipDir != "east"  && !CheckSide(cfg.east,  pos.x + 1, pos.y, "west"))  return false;
        if (skipDir != "west"  && !CheckSide(cfg.west,  pos.x - 1, pos.y, "east"))  return false;
        return hasDoor; // must also have at least one actual door
    }

    void PlaceStairsOnEdge(GameObject parent, int levelIndex, int connStartX, int connStartZ)
    {
        List<GameObject> stairsPrefabs = new List<GameObject>();
        foreach (GameObject prefab in allTilePrefabs)
        {
            if (prefab != null && prefab.name.Contains("Stairs"))
                stairsPrefabs.Add(prefab);
        }

        if (stairsPrefabs.Count == 0) return;

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

        List<(int x, int z, GameObject prefab)> validPlacements    = new List<(int, int, GameObject)>();
        List<(int x, int z, GameObject prefab)> fallbackPlacements  = new List<(int, int, GameObject)>();

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                // Only check perimeter positions
                if (x != 0 && x != dungeonWidth - 1 && z != 0 && z != dungeonHeight - 1) continue;
                if (placedTiles[x, z] == null) continue;

                bool nearCorner = (x <= 2 || x >= dungeonWidth - 3) &&
                                  (z <= 2 || z >= dungeonHeight - 3);
                if (nearCorner) continue;

                Vector2Int inward = DebugEntranceTile(new Vector2Int(x, z));
                if (!IsInBounds(inward.x, inward.y)) continue;
                if (!reachable.Contains(inward)) continue;
                TileConfig inwardCfg = placedConfigs[inward.x, inward.y];
                if (inwardCfg == null) continue;

                if (levelIndex > 0)
                {
                    int dist = Mathf.Abs(inward.x - connStartX) + Mathf.Abs(inward.y - connStartZ);
                    if (dist < 4) continue;
                }

                string stairsDir = x == 0 ? "west" :
                                   x == dungeonWidth - 1 ? "east" :
                                   z == 0 ? "south" : "north";
                if (inwardCfg.IsRoomTile() && !IsValidSafeRoomEntrance(inward, inwardCfg, stairsDir))
                    continue;

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

        if (validPlacements.Count == 0 && fallbackPlacements.Count > 0)
        {
            validPlacements = fallbackPlacements;
            Debug.LogWarning($"Level {levelIndex}: No room-tile inward position found for stairs — using fallback.");
        }

        if (validPlacements.Count > 0)
        {
            var chosen = validPlacements[Random.Range(0, validPlacements.Count)];

            SafeDestroy(placedTiles[chosen.x, chosen.z]);

            float stairsY = (levelIndex + 1) * -levelHeight;
            Vector3 stairsPos = new Vector3(chosen.x * tileSize, stairsY, chosen.z * tileSize);
            placedTiles[chosen.x, chosen.z] = Instantiate(chosen.prefab, stairsPos, Quaternion.identity, parent.transform);
            placedTiles[chosen.x, chosen.z].name = $"Tile_{chosen.x}_{chosen.z}_{chosen.prefab.name}";
            if (tileConfigs.ContainsKey(chosen.prefab.name)) placedConfigs[chosen.x, chosen.z] = tileConfigs[chosen.prefab.name];

            levelVisibility?.RegisterStairway(placedTiles[chosen.x, chosen.z]);
            stairsPositions.Add(new Vector2Int(chosen.x, chosen.z));
        }
        else
        {
            Debug.LogWarning($"Level {levelIndex}: Could not find valid position for stairs on edge");
        }
    }

    [ContextMenu("Clear Dungeon")]
    public void ClearDungeon()
    {
        DungeonNavMeshSetup navSetup = GetComponent<DungeonNavMeshSetup>();
        if (navSetup != null) navSetup.ClearAllSpawns();

        SafeRoomSetup safeRoom = FindObjectOfType<SafeRoomSetup>();
        if (safeRoom != null) safeRoom.ClearAll();

        SpawnRoomSetup spawnRoom = FindObjectOfType<SpawnRoomSetup>();
        if (spawnRoom != null) spawnRoom.ClearAll();

        CodeNumberManager codeNumbers = FindObjectOfType<CodeNumberManager>();
        if (codeNumbers != null) codeNumbers.ClearAll();

        ComputerRoomSetup computerRoom = FindObjectOfType<ComputerRoomSetup>();
        if (computerRoom != null) computerRoom.ClearAll();

        BatterySpawnSetup batterySpawn = FindObjectOfType<BatterySpawnSetup>();
        if (batterySpawn != null) batterySpawn.ClearAll();

        HiddenRoomSetup hiddenRoom = HiddenRoomSetup.Instance;
        if (hiddenRoom == null) hiddenRoom = FindObjectOfType<HiddenRoomSetup>();
        if (hiddenRoom != null) hiddenRoom.ClearAll();

        DetonationRoomSetup detRoom = DetonationRoomSetup.Instance;
        if (detRoom == null) detRoom = FindObjectOfType<DetonationRoomSetup>();
        if (detRoom != null) detRoom.ClearAll();

        IntroRoomSetup introRoom = FindObjectOfType<IntroRoomSetup>();
        if (introRoom != null) introRoom.ClearRoom();

        levelVisibility?.ClearAll();

        if (levelParents != null)
        {
            foreach (GameObject levelParent in levelParents)
                SafeDestroy(levelParent);
            levelParents.Clear();
        }

        if (placedTiles != null)
            for (int x = 0; x < placedTiles.GetLength(0); x++)
                for (int z = 0; z < placedTiles.GetLength(1); z++)
                    SafeDestroy(placedTiles[x, z]);

        allLevelConfigs.Clear();
        allLevelReachable.Clear();
        stairsPositions.Clear();

        placedTiles = null;
        placedConfigs = null;
    }

    void SetupKeypads()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("Player not found! Make sure player has 'Player' tag");
            return;
        }

        GameObject promptUI = GameObject.Find("KeypadPrompt");
        TMPro.TMP_Text promptText = promptUI != null ? promptUI.GetComponent<TMPro.TMP_Text>() : null;

        NavKeypad.KeypadPlayerInteraction[] keypads = FindObjectsOfType<NavKeypad.KeypadPlayerInteraction>();

        foreach (var kp in keypads)
        {
            typeof(NavKeypad.KeypadPlayerInteraction)
                .GetField("player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(kp, player);

            typeof(NavKeypad.KeypadPlayerInteraction)
                .GetField("interactionPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(kp, promptUI);

            typeof(NavKeypad.KeypadPlayerInteraction)
                .GetField("promptText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(kp, promptText);

            NavKeypad.Keypad keypadLogic = kp.GetComponentInParent<NavKeypad.Keypad>();
            if (keypadLogic == null) keypadLogic = kp.GetComponent<NavKeypad.Keypad>();

            if (keypadLogic != null)
            {
                NavKeypad.SlidingDoor door = kp.GetComponentInParent<NavKeypad.SlidingDoor>();
                if (door == null) door = kp.GetComponentInChildren<NavKeypad.SlidingDoor>();

                if (door != null)
                {
                    keypadLogic.OnAccessGranted.RemoveAllListeners();
                    keypadLogic.OnAccessGranted.AddListener(door.OpenDoor);
                }
                else
                {
                    Debug.LogWarning($"SetupKeypads: No SlidingDoor found near keypad {kp.name}. Check the stairs prefab hierarchy.");
                }
            }
        }

    }
}