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
    [Tooltip("If more than this many tiles remain isolated after the standard repair passes, extra passes run — and if they still exceed the threshold after that, the level is regenerated.")]
    public int isolatedTileThreshold = 10;
    [Tooltip("How many times to retry generating a level before accepting the best available result. Higher values reduce the chance of exceeding the isolated tile threshold but increase generation time.")]
    public int maxGenerationAttempts = 8;

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
    private List<(GameObject parent, int index, TileConfig[,] configs, int connStartX, int connStartZ)> pendingNavMeshLevels
        = new List<(GameObject, int, TileConfig[,], int, int)>();

    private Dictionary<string, TileConfig> tileConfigs;
    private GameObject[,] placedTiles;
    private TileConfig[,] placedConfigs;
    private bool isRepairing = false; // Relaxes some placement rules during repair passes
    // Set to the safe-room entrance tile position while generating a level that has one.
    // Used by IsCompatibleWithNeighbors to prevent Left/Right edges from facing into the entrance.
    private Vector2Int safeRoomEntrancePos = new Vector2Int(-1, -1);
    private DungeonLevelVisibility levelVisibility; // Cached for level show/hide management

    // Best-attempt tracking across retries — keeps whichever generation had the fewest isolated tiles
    // so the final fallback is always the best we produced, not just the last.
    private int         bestAttemptIsolatedCount;
    private GameObject  bestAttemptLevelParent;
    private GameObject[,] bestAttemptPlacedTiles;
    private TileConfig[,]  bestAttemptPlacedConfigs;
    private List<Vector2Int> stairsPositions = new List<Vector2Int>(); // Track where stairs were placed
    private Dictionary<int, TileConfig[,]> allLevelConfigs = new Dictionary<int, TileConfig[,]>();
    // Reachable tile sets per level — used by DungeonNavMeshSetup to filter spawn zones.
    private Dictionary<int, HashSet<Vector2Int>> allLevelReachable = new Dictionary<int, HashSet<Vector2Int>>();
    public HashSet<Vector2Int> GetLevelReachable(int levelIndex)
        => allLevelReachable.TryGetValue(levelIndex, out var set) ? set : new HashSet<Vector2Int>();

    /// <summary>
    /// Removes a tile from the dungeon grid and destroys its GameObject.
    /// Called by DetonationRoomSetup so the 2×2 block it occupies is excluded from
    /// code-number, locker, and battery placement (all of which skip null configs).
    /// </summary>
    public void RemoveTile(int x, int z)
    {
        if (!IsInBounds(x, z)) return;
        SafeDestroy(placedTiles[x, z]);
        placedTiles[x, z]  = null;
        placedConfigs[x, z] = null;
    }

    /// <summary>
    /// Places a Tiles_01_Fill tile at the given grid position.
    /// Called by DetonationRoomSetup after clearing the inward approach tiles so the
    /// player has solid, wall-free floor to walk on right up to the door opening.
    /// Fill tiles are always passable (CanWalkBetween treats them as open floor) so
    /// the flood-fill reachability traversal runs straight through them.
    /// </summary>
    public void PlaceFillTileAt(int x, int z, GameObject parent)
    {
        if (!IsInBounds(x, z)) return;

        GameObject fillPrefab = System.Array.Find(
            allTilePrefabs, p => p != null && p.name == "Tiles_01_Fill");

        if (fillPrefab == null)
        {
            Debug.LogWarning("[ProceduralDungeonGenerator] Tiles_01_Fill prefab not found " +
                             "in allTilePrefabs — cannot place fill tile for detonation room.");
            return;
        }

        // Remove whatever is there first (RemoveTile already guards IsInBounds)
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

        // Cache visibility manager (on same GO — safe to do here before generation starts)
        levelVisibility = GetComponent<DungeonLevelVisibility>();

        // Seed Unity's RNG so generation is deterministic and reproducible.
        // Loading a save re-uses the stored seed → identical dungeon layout every time.
        // Starting a new game generates a fresh seed and records it in SaveGameManager.
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

            // Hide all levels except level 0 and plant stairway visibility triggers.
            levelVisibility?.InitialHide(levelHeight, dungeonWidth, stairsPositions, levelParents, tileSize);
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
                                    dungeonWidth, dungeonHeight, tileSize, levelHeight,
                                    entry.connStartX, entry.connStartZ);
        }
        pendingNavMeshLevels.Clear();

        // Hide all levels except level 0 and plant stairway visibility triggers.
        levelVisibility?.InitialHide(levelHeight, dungeonWidth, stairsPositions, levelParents, tileSize);

        // Position the intro room prefab above the level 0 spawn room.
        SpawnRoomSetup introSpawnRef = FindObjectOfType<SpawnRoomSetup>();
        IntroRoomSetup introRoom = FindObjectOfType<IntroRoomSetup>();
        if (introRoom != null && introSpawnRef != null)
            introRoom.PlaceIntroRoom(introSpawnRef);

        // Move the player into level 0's spawn room now that generation and NavMesh are ready
        if (GameManager.Instance != null)
            GameManager.Instance.PlacePlayerAtSpawnRoom();
    }

    void GenerateLevel(int levelIndex, int attemptNumber = 1)
    {
        // GenerateLevel start — no log needed, PrintLevelDebug summarises the result

        // Create parent for this level.
        // Use indexed assignment so retries never grow the list — levelParents[levelIndex]
        // always holds the CURRENT attempt's parent, never a stale entry from a previous try.
        GameObject levelParent = new GameObject($"Level_{levelIndex}");
        levelParent.transform.parent = transform;
        levelParent.transform.position = new Vector3(0, levelIndex * -levelHeight, 0);
        while (levelParents.Count <= levelIndex)
            levelParents.Add(null);
        levelParents[levelIndex] = levelParent;

        // Reset arrays for this level
        placedTiles = new GameObject[dungeonWidth, dungeonHeight];
        placedConfigs = new TileConfig[dungeonWidth, dungeonHeight];

        // Determine starting position for this level
        int startX, startZ;
        if (levelIndex > 0 && stairsPositions.Count >= levelIndex)
        {
            // Start from where the previous level's stairs land
            Vector2Int landingPos = stairsPositions[levelIndex - 1];
            startX = landingPos.x;
            startZ = landingPos.y;
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

            // Force-place 5 tiles leading inward from stairs.
            // i=0 (perimeter/stairs position): already occupied by the stairway prefab from
            //     the level above — just mark visited so BFS never overwrites it.
            // i=1 (safe room entrance): requires Open toward stairs (so the player can walk in)
            //     AND forward Open toward dungeon (so BFS connects through, not into a pocket).
            // i=2..4 (corridor into dungeon): TryPlaceCompatibleTile ensures edge matching.
            //     Extending to 5 pushes the BFS frontier away from the constrained perimeter
            //     edge, preventing the frontier from dying in a small corner pocket.
            for (int i = 0; i < 5; i++)
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
                        {
                            PlaceStaircaseEntranceTile(px, pz, pathDir, levelParent);
                            // Record this as the safe-room entrance so IsCompatibleWithNeighbors
                            // can block Left/Right edges from facing into it during BFS.
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

        // BFS complete — clear the safe-room entrance guard so normal repair passes
        // are not constrained by it (isolated-tile repair may place tiles there freely).
        safeRoomEntrancePos = new Vector2Int(-1, -1);

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

        List<Vector2Int> isolated = FindIsolatedTiles(connStartX, connStartZ);

        int repairPass = 0;
        int maxRepairPasses = 7; // Try up to 7 repair attempts for stubborn isolated areas

        isRepairing = true; // Relax placement rules during repair (e.g. sealed-from-dungeon checks)
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

            // Re-check connectivity after this pass
            isolated = FindIsolatedTiles(connStartX, connStartZ);

            if (isolated.Count == 0) break;

            // No progress — stop early rather than burning through remaining passes uselessly
            if (repaired == 0) break;
        }
        isRepairing = false;

        // Extra repair phase: if isolated count is still above the threshold after standard passes,
        // run up to 5 more passes before handing off to validation.
        // This gives the new Strategy 3 (and Strategy 2) more chances on stubborn tiles without
        // inflating the standard pass count for levels that need very few repairs.
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
                if (repaired == 0) break; // No progress — stop early
            }
            isRepairing = false;
        }

        // Best-attempt tracking: reset on the first try for this level, then save this
        // attempt if it has fewer isolated tiles than any attempt seen so far.
        // The best parent is kept alive (not destroyed on retry) so we can fall back to it.
        if (attemptNumber == 1)
        {
            bestAttemptIsolatedCount = int.MaxValue;
            bestAttemptLevelParent   = null;
            bestAttemptPlacedTiles   = null;
            bestAttemptPlacedConfigs = null;
        }
        if (isolated.Count < bestAttemptIsolatedCount)
        {
            // New best — destroy the old saved parent and store this one
            if (bestAttemptLevelParent != null && bestAttemptLevelParent != levelParent)
                DestroyImmediate(bestAttemptLevelParent);
            bestAttemptLevelParent   = levelParent;
            bestAttemptIsolatedCount = isolated.Count;
            // Shallow-clone the arrays — the GOs they reference are children of bestAttemptLevelParent
            // which we are keeping alive, so the references remain valid.
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

                // All retries exhausted and isolated count still exceeds the threshold.
                // ORPHAN every remaining isolated tile — null their entries in the
                // tracking arrays so downstream gameplay systems (code numbers, lockers,
                // batteries, computer room, etc.) skip these positions and nothing spawns
                // in an unreachable room.
                //
                // Crucially, we KEEP the GameObject alive (not SafeDestroy) so the visible
                // floor/walls remain intact. Previously this destroyed the tile outright,
                // leaving a visible HOLE in the grid — most often at corner cells, which
                // are the easiest to isolate (only 2 neighbours, both can roll walls
                // facing inward). A sealed-but-visible tile is better than a missing one:
                //   • The player can't reach it either way (it's still isolated)
                //   • No gameplay is placed there (arrays are nulled, same as before)
                //   • The GameObject is still a child of levelParent, so ClearDungeon()
                //     cleans it up correctly on regenerate
                //   • NavMesh bake still works (walls act as NavMesh obstacles)
                List<Vector2Int> finalIsolated = FindIsolatedTiles(connStartX, connStartZ);
                if (finalIsolated.Count > 0)
                {
                    foreach (Vector2Int isoPos in finalIsolated)
                    {
                        GameObject orphan = placedTiles[isoPos.x, isoPos.y];
                        if (orphan != null)
                        {
                            // Rename for editor/debug clarity — this tile is no longer
                            // addressable via the generator's x/z grid lookups.
                            orphan.name = $"Orphan_{isoPos.x}_{isoPos.y}_Isolated";
                        }
                        placedTiles[isoPos.x, isoPos.y]   = null;
                        placedConfigs[isoPos.x, isoPos.y] = null;
                    }
                    Debug.LogWarning($"[ProceduralDungeonGenerator] Level {levelIndex}: orphaned {finalIsolated.Count} unreachable isolated tile(s) (kept visible, excluded from gameplay) after all {maxGenerationAttempts} attempts failed to meet threshold ({isolatedTileThreshold}).");
                }

                // Tracking fields are cleared in the unconditional block below.
            }
        }

        // Validation passed (or we restored the best attempt above). If an earlier attempt's
        // parent is still alive as the saved "best", destroy it now — we don't need it and
        // leaving it in the scene would create duplicate tile geometry with no doors or sealers.
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

        // Level-specific spawn config:
        //   Level 0: 4 wall numbers only
        //   Level 1: 3 wall numbers + computer terminal (slot 3)
        //   Level 2: 2 wall numbers + hidden room (slot 2) + computer terminal (slot 3)
        int wallNumberCount = levelIndex == 0 ? 4 : (levelIndex == 1 ? 3 : 2);
        bool spawnHiddenRoom = levelIndex >= 2;
        bool spawnComputer   = levelIndex >= 1;

        // Computer room setup runs FIRST so HiddenRoomSetup can read its tile and avoid it.
        // ComputerRoomSetup does not depend on CodeNumberManager or HiddenRoomSetup.
        ComputerRoomSetup computerRoom = FindObjectOfType<ComputerRoomSetup>();
        if (computerRoom != null && spawnComputer)
            computerRoom.SetupLevel(this, levelIndex, levelParent, spawnRoom, connStartX, connStartZ);

        // Hidden room setup runs BEFORE CodeNumberManager so it can register the
        // hidden-room tile as excluded (preventing a duplicate wall number there).
        // Runs AFTER ComputerRoomSetup so it can also exclude the computer room tile.
        HiddenRoomSetup hiddenRoom = HiddenRoomSetup.Instance;
        if (hiddenRoom == null) hiddenRoom = FindObjectOfType<HiddenRoomSetup>();
        if (hiddenRoom != null && spawnHiddenRoom)
            hiddenRoom.SetupLevel(this, levelIndex, levelParent, spawnRoom, connStartX, connStartZ);

        // Detonation room — only on the last level (no stairway level).
        // Runs BEFORE CodeNumberManager so that nulled tiles are excluded from number placement.
        // Runs AFTER ComputerRoom and HiddenRoom so it can exclude their tiles.
        if (levelIndex == numberOfLevels - 1)
        {
            DetonationRoomSetup detRoom = DetonationRoomSetup.Instance;
            if (detRoom == null) detRoom = FindObjectOfType<DetonationRoomSetup>();
            if (detRoom != null)
                detRoom.SetupLevel(this, levelIndex, levelParent,
                                   spawnRoom, computerRoom, hiddenRoom,
                                   connStartX, connStartZ);
        }

        // Notify CodeNumberManager so it can spawn collectible code numbers for this level.
        // Always use the singleton Instance so InitializeForLevel and OnDigitCollected
        // both operate on the exact same object (FindObjectOfType can return a duplicate
        // that is still alive but already scheduled for Destroy, causing a state mismatch).
        CodeNumberManager codeManager = CodeNumberManager.Instance;
        if (codeManager == null) codeManager = FindObjectOfType<CodeNumberManager>();
        if (codeManager != null)
            codeManager.InitializeForLevel(this, levelIndex, connStartX, connStartZ, wallNumberCount);

        // Spawn lockers AFTER CodeNumberManager so we can exclude tiles that already
        // have code numbers on their walls — prevents lockers blocking collectible digits.
        LockerSetup lockerSetup = FindObjectOfType<LockerSetup>();
        if (lockerSetup != null)
        {
            NPCSpawnManager npcSpawnMgr = FindObjectOfType<NPCSpawnManager>();
            lockerSetup.SetupLevel(this, levelIndex, levelParent,
                                   spawnRoom, hiddenRoom, computerRoom, safeRoom, npcSpawnMgr);
        }

        // Scatter battery pickups and any extra floor items (flashlight, etc.) for this level.
        BatterySpawnSetup batterySpawn = FindObjectOfType<BatterySpawnSetup>();
        if (batterySpawn != null)
            batterySpawn.SetupLevel(this, levelIndex, levelParent,
                                    spawnRoom, safeRoom, hiddenRoom, computerRoom);

        // Now that CodeNumberManager has generated digits, spawn the hidden room's number (slot 2)
        if (hiddenRoom != null && spawnHiddenRoom)
            hiddenRoom.SpawnNumberAfterInit(levelIndex, tileSize);

        PrintLevelDebug(levelIndex, connStartX, connStartZ);

        // Save this level's configs and reachable set (used by DungeonNavMeshSetup to
        // filter spawn zones — must be captured now while placedTiles/placedConfigs are
        // still set to this level, before the next GenerateLevel call resets them).
        TileConfig[,] levelConfigsCopy = new TileConfig[dungeonWidth, dungeonHeight];
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
                levelConfigsCopy[x, z] = placedConfigs[x, z];
        allLevelConfigs[levelIndex] = levelConfigsCopy;
        allLevelReachable[levelIndex] = FloodFillReachable(connStartX, connStartZ);

        // Setup NavMesh and NPC spawning for this level.
        // At runtime (deferNavMesh = true) the bake is deferred to GenerateDungeonCoroutine()
        // so it fires after Unity has registered all MeshRenderers from this frame's Instantiate calls.
        // In editor mode (deferNavMesh = false) bake immediately as before.
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

        // Seal all Wall edges with invisible collider barriers to prevent wall walk-through
        DungeonWallSealer sealer = FindObjectOfType<DungeonWallSealer>();
        if (sealer != null)
            sealer.SealLevel(this, levelIndex, levelParent);

        // Register with the level visibility manager.
        // Static batching and InitialHide are called by GenerateDungeon AFTER all levels
        // are complete, so this just records the parent for later show/hide management.
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
            return true;
        }

        // Phase 3: Fallback placement
        bool isPerimeter = (x == 0 || x == dungeonWidth - 1 || z == 0 || z == dungeonHeight - 1);

        if (isPerimeter)
        {
            // PERIMETER FALLBACK: Find any tile with Wall on outward edge(s).
            // A mismatched real tile is better than a fill (no walls) or empty gap.
            // CRITICAL: the tile must also have at least one non-Wall inward edge so it
            // can connect to the rest of the dungeon. A fully-sealed tile (all edges Wall)
            // placed at a corner would be permanently isolated and trigger the stacking bug
            // in TryRepairIsolatedTile — never place one here.
            List<GameObject> perimeterSafe = new List<GameObject>();
            foreach (GameObject prefab in allTilePrefabs)
            {
                if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
                if (prefab.name.Contains("Stairs") || prefab.name == "Tiles_01_Fill") continue;
                TileConfig cfg = tileConfigs[prefab.name];

                // Must have Wall on every outward-facing edge
                if (x == 0              && cfg.west  != EdgeType.Wall) continue;
                if (x == dungeonWidth-1 && cfg.east  != EdgeType.Wall) continue;
                if (z == 0              && cfg.south != EdgeType.Wall) continue;
                if (z == dungeonHeight-1 && cfg.north != EdgeType.Wall) continue;

                // Must have at least one Open inward-facing edge so the tile isn't
                // a sealed box that will always be isolated and can't be repaired.
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

            // LAST RESORT for perimeter: if no tile with an inward-open edge exists,
            // accept a fully-walled tile rather than leaving the cell empty. This was
            // the root cause of missing-corner bugs: corners have only 2 neighbours so
            // their candidate pool is smallest, and rolling an empty pool at the previous
            // stage left a literal hole in the grid.
            //
            // A sealed-corner tile is isolated (can't connect inward) but that's fine —
            // the orphan logic at the end of GenerateLevel handles isolated tiles safely
            // by keeping their GameObject visible while nulling their array entries so
            // nothing gameplay-wise spawns on them. The comment above about the "stacking
            // bug in TryRepairIsolatedTile" no longer applies since orphan replaces the
            // destroy fallback.
            List<GameObject> perimeterLastResort = new List<GameObject>();
            foreach (GameObject prefab in allTilePrefabs)
            {
                if (prefab == null || !tileConfigs.ContainsKey(prefab.name)) continue;
                if (prefab.name.Contains("Stairs") || prefab.name == "Tiles_01_Fill") continue;
                TileConfig cfg = tileConfigs[prefab.name];

                // Outward-facing edges must still be walls — this is the non-negotiable
                // "no walk-off hazard" rule. We ONLY relax the hasInwardOpen requirement.
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

        // Interior: use Fill tile as last resort
        if (!isPerimeter)
        {
            GameObject fillTile = System.Array.Find(allTilePrefabs, p => p != null && p.name == "Tiles_01_Fill");
            if (fillTile != null)
            {
                PlaceTile(x, z, fillTile, parent);
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
                            return false;
                        }
                    }
                }
            }
        }

        // Safe-room entrance guard: if this tile is being placed directly adjacent to
        // the stairway entrance tile, its face toward that entrance must not be Left or
        // Right. Those partial-wall edges create a passable gap into the safe room that
        // can't be sealed with a sliding door and isn't blocked by any geometry.
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

    // Gets the reciprocal edge type from a neighbour tile, treating Fill as Open.
    EdgeType GetReciprocal(TileConfig adj, string faceFromNeighbour)
    {
        if (adj.tileName == "Tiles_01_Fill") return EdgeType.Open;
        return faceFromNeighbour == "south" ? adj.south :
               faceFromNeighbour == "north" ? adj.north :
               faceFromNeighbour == "west"  ? adj.west  : adj.east;
    }

    // Returns true if every Open side of the entrance tile (except the stairs-facing side)
    // is compatible with a safe room door — i.e. the neighbour's reciprocal is not Left or
    // Right (those are passable partial-wall gaps that can't be doored and can't be sealed).
    // Also returns false if there is no doorable side at all (room would have no exit).
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

                // Avoid corners: stairs near grid corners force the next level's BFS
                // to start on two constrained perimeter edges at once, frequently
                // trapping the frontier in a tiny pocket with most tiles isolated.
                bool nearCorner = (x <= 2 || x >= dungeonWidth - 3) &&
                                  (z <= 2 || z >= dungeonHeight - 3);
                if (nearCorner) continue;

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

                // The entrance tile's "back" side points toward the stairs — SafeRoomSetup
                // skips that face. Every other Open side must either get a door (neighbour
                // reciprocal is Open/Center) or be naturally sealed (neighbour Wall).
                // Left/Right reciprocals create passable gaps with no door and can't be
                // blocked, so the whole candidate is rejected when any such gap exists.
                // The check also requires at least one actual door side.
                string stairsDir = x == 0 ? "west" :
                                   x == dungeonWidth - 1 ? "east" :
                                   z == 0 ? "south" : "north";
                if (inwardCfg.IsRoomTile() && !IsValidSafeRoomEntrance(inward, inwardCfg, stairsDir))
                    continue;

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

            // Register with visibility manager so the stairway can be reparented to the
            // persistent Stairways root in InitialHide — keeps it visible when its level hides.
            levelVisibility?.RegisterStairway(placedTiles[chosen.x, chosen.z]);

            // Save stairs position for next level to use as entrance
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

        // Clear all computer rooms across all levels
        ComputerRoomSetup computerRoom = FindObjectOfType<ComputerRoomSetup>();
        if (computerRoom != null)
            computerRoom.ClearAll();

        // Clear battery pickups and extra floor items
        BatterySpawnSetup batterySpawn = FindObjectOfType<BatterySpawnSetup>();
        if (batterySpawn != null)
            batterySpawn.ClearAll();

        // Clear hidden room breakable walls, goggles pickup, and excluded tile registry
        HiddenRoomSetup hiddenRoom = HiddenRoomSetup.Instance;
        if (hiddenRoom == null) hiddenRoom = FindObjectOfType<HiddenRoomSetup>();
        if (hiddenRoom != null)
            hiddenRoom.ClearAll();

        // Clear detonation room prefab and state
        DetonationRoomSetup detRoom = DetonationRoomSetup.Instance;
        if (detRoom == null) detRoom = FindObjectOfType<DetonationRoomSetup>();
        if (detRoom != null)
            detRoom.ClearAll();

        // Clear intro room (re-placed after each generation)
        IntroRoomSetup introRoom = FindObjectOfType<IntroRoomSetup>();
        if (introRoom != null)
            introRoom.ClearRoom();

        // Clear level visibility state before level parents are destroyed
        levelVisibility?.ClearAll();

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

        // Clear all multi-level data
        allLevelConfigs.Clear();
        allLevelReachable.Clear();
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
                }
                else
                {
                    Debug.LogWarning($"SetupKeypads: No SlidingDoor found near keypad {kp.name}. Check the stairs prefab hierarchy.");
                }
            }
        }

    }
}