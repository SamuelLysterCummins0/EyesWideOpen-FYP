using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sets up the hidden-room mechanic each level.
///
/// Uses four directional breakable-wall prefabs (one per cardinal direction),
/// the same way SpawnRoomSetup uses doorNorth/doorEast/doorSouth/doorWest.
/// Assign each prefab in the Inspector — each should already be rotated /
/// modelled to face the correct direction in its own asset.
///
/// Flow each level:
///   1. Find a room tile far from spawn with ≥1 Wall edge (for the code number)
///      and ≥1 Open edge (to seal).
///   2. Register it with CodeNumberManager so normal wall-number spawning skips it.
///   3. Seal every Open edge of that tile with the matching directional prefab.
///   4. Spawn code-number slot 2 on a Wall edge inside.
///   5. Scatter decoy breakable walls on solid Wall edges elsewhere.
///   6. Drop the goggles pickup on a random reachable tile.
///
/// Called by ProceduralDungeonGenerator BEFORE CodeNumberManager.InitializeForLevel.
/// </summary>
public class HiddenRoomSetup : MonoBehaviour
{
    public static HiddenRoomSetup Instance { get; private set; }

    [Header("Breakable Wall Prefabs (one per direction, same as SpawnRoom doors)")]
    [Tooltip("Wall facing south — seals the North edge of a tile (player approaches from south).")]
    public GameObject breakableWallNorth;

    [Tooltip("Wall facing north — seals the South edge of a tile.")]
    public GameObject breakableWallSouth;

    [Tooltip("Wall facing west — seals the East edge of a tile.")]
    public GameObject breakableWallEast;

    [Tooltip("Wall facing east — seals the West edge of a tile.")]
    public GameObject breakableWallWest;

    [Header("Goggles Pickup")]
    [Tooltip("Pickup prefab with ItemPickUp + Goggles Item ScriptableObject assigned.")]
    public GameObject gogglePickupPrefab;

    [Header("Powerbox")]
    [Tooltip("Powerbox prefab (must have PowerboxInteraction component). Spawns on a perimeter wall on the outage level.")]
    public GameObject powerboxPrefab;
    [Tooltip("How far from the wall surface the powerbox is offset inward (same as code numbers).")]
    public float powerboxWallOffset = 0.12f;
    [Tooltip("Raycast distance used to find the wall surface for powerbox placement.")]
    public float powerboxWallRaycast = 2.5f;

    [Header("Settings")]
    [Tooltip("World-space distance from spawn point — tiles closer than this cannot be the hidden room.")]
    public float minDistanceFromSpawn = 14f;

    [Tooltip("Goggles pickup won't spawn within this world-space distance of the spawn point.")]
    public float minGoggleSpawnDistance = 8f;

    // ── Per-level tracked objects ──────────────────────────────────────────────
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    // Cached after SetupLevel so SpawnNumberAfterInit can run post-CodeNumberManager
    private Vector2Int pendingHiddenPos = new Vector2Int(-1, -1);
    private ProceduralDungeonGenerator pendingGen;

    // Tile where the powerbox was placed — goggles spawn here instead of a random tile
    private Vector2Int powerboxTile = new Vector2Int(-1, -1);

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    public void SetupLevel(
        ProceduralDungeonGenerator gen,
        int levelIndex,
        GameObject levelParent,
        SpawnRoomSetup spawnRoom,
        int connStartX, int connStartZ)
    {
        // Only reset the excluded-tile registry so this level gets a fresh exclusion.
        // Do NOT call ClearAll() here — that would destroy previous levels' hidden rooms.
        // ClearAll() is reserved for ProceduralDungeonGenerator.ClearDungeon().
        if (CodeNumberManager.Instance != null)
            CodeNumberManager.Instance.ClearExcludedTiles();

        float tileSize = gen.TileSize;
        float levelY   = levelIndex * -gen.LevelHeight;

        Vector3 spawnRef = spawnRoom != null && spawnRoom.HasSpawnPoint(levelIndex)
            ? spawnRoom.GetSpawnPoint(levelIndex)
            : new Vector3(connStartX * tileSize, 0f, connStartZ * tileSize);

        List<Vector2Int> reachable = gen.GetReachableTilePositions(connStartX, connStartZ);
        if (reachable.Count < 5)
        {
            Debug.LogWarning("[HiddenRoomSetup] Too few reachable tiles — skipping.");
            return;
        }

        ShuffleList(reachable);

        // 1. Find hidden room tile
        Vector2Int hiddenPos = FindHiddenRoomTile(gen, reachable, spawnRef, tileSize);
        if (hiddenPos.x < 0)
        {
            Debug.LogWarning("[HiddenRoomSetup] Could not find a suitable hidden-room tile.");
            return;
        }

        // 2. Exclude from normal code-number placement
        if (CodeNumberManager.Instance != null)
            CodeNumberManager.Instance.ExcludeTile(hiddenPos);

        // 3. Seal all Open edges with directional breakable walls
        SealRoom(gen, hiddenPos, levelParent, tileSize);

        // 4. Cache for SpawnNumberAfterInit — must run AFTER CodeNumberManager.InitializeForLevel
        //    so digits are available. ProceduralDungeonGenerator calls SpawnNumberAfterInit explicitly.
        pendingHiddenPos = hiddenPos;
        pendingGen       = gen;

        // 5. Spawn the powerbox on a perimeter wall — must happen before goggles so
        //    we know which tile to co-locate the goggles pickup with.
        powerboxTile = new Vector2Int(-1, -1);
        if (powerboxPrefab == null)
        {
            Debug.LogWarning("[HiddenRoomSetup] powerboxPrefab is NOT assigned in the Inspector — powerbox will not spawn.");
        }
        else
        {
            powerboxTile = FindPowerboxTile(gen, hiddenPos, spawnRef, tileSize);
            Debug.Log($"[HiddenRoomSetup] L{levelIndex}: powerbox tile = {powerboxTile}");

            if (powerboxTile.x >= 0)
            {
                SpawnPowerbox(gen, powerboxTile, levelParent, tileSize, levelIndex, levelY);

                if (PowerManager.Instance == null)
                    Debug.LogError("[HiddenRoomSetup] PowerManager.Instance is NULL — add a PowerManager component to a GameObject in the scene!");
                else
                {
                    PowerManager.Instance.RegisterOutageLevel(levelIndex, levelParent);
                    Debug.Log($"[HiddenRoomSetup] L{levelIndex}: RegisterOutageLevel called on PowerManager.");
                }
            }
            else
            {
                Debug.LogWarning($"[HiddenRoomSetup] L{levelIndex}: FindPowerboxTile returned no valid tile.");
            }
        }

        // 6. Drop the goggles — prefer the powerbox tile so they're found together
        SpawnGoggles(gen, reachable, hiddenPos, spawnRef, levelParent, tileSize, levelY, levelIndex);
    }

    // ── Tile selection ─────────────────────────────────────────────────────────

    private Vector2Int FindHiddenRoomTile(
        ProceduralDungeonGenerator gen,
        List<Vector2Int> reachable,
        Vector3 spawnRef,
        float tileSize)
    {
        foreach (Vector2Int pos in reachable)
        {
            Vector3 world = new Vector3(pos.x * tileSize, 0f, pos.y * tileSize);
            if (Vector3.Distance(world, spawnRef) < minDistanceFromSpawn) continue;

            ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
            if (cfg == null || !cfg.IsRoomTile()) continue;

            // Needs a Wall edge for the code number
            bool hasWall =
                cfg.north == ProceduralDungeonGenerator.EdgeType.Wall ||
                cfg.south == ProceduralDungeonGenerator.EdgeType.Wall ||
                cfg.east  == ProceduralDungeonGenerator.EdgeType.Wall ||
                cfg.west  == ProceduralDungeonGenerator.EdgeType.Wall;
            if (!hasWall) continue;

            // Needs an Open edge to seal
            if (cfg.GetOpeningCount() < 1) continue;

            // Needs at least one directional prefab assigned for its Open edge(s)
            if (!HasPrefabForOpenEdges(cfg)) continue;

            return pos;
        }
        return new Vector2Int(-1, -1);
    }

    // Make sure we have a prefab for every Open edge we need to seal
    private bool HasPrefabForOpenEdges(ProceduralDungeonGenerator.TileConfig cfg)
    {
        if (cfg.north == ProceduralDungeonGenerator.EdgeType.Open && breakableWallNorth == null) return false;
        if (cfg.south == ProceduralDungeonGenerator.EdgeType.Open && breakableWallSouth == null) return false;
        if (cfg.east  == ProceduralDungeonGenerator.EdgeType.Open && breakableWallEast  == null) return false;
        if (cfg.west  == ProceduralDungeonGenerator.EdgeType.Open && breakableWallWest  == null) return false;
        return true;
    }

    // ── Room sealing ───────────────────────────────────────────────────────────

    private void SealRoom(
        ProceduralDungeonGenerator gen,
        Vector2Int pos,
        GameObject parent,
        float tileSize)
    {
        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
        if (cfg == null) return;

        GameObject tile = gen.GetPlacedTile(pos.x, pos.y);
        if (tile == null) return;

        Vector3 centre = new Vector3(pos.x * tileSize, tile.transform.position.y, pos.y * tileSize);
        float   half   = tileSize * 0.5f;

        // One directional prefab per face — identical pattern to SpawnRoomSetup.TryPlaceDoor
        TryPlaceBreakableWall(cfg.north, breakableWallNorth, centre + new Vector3(0, 0,  half), parent, "HiddenWall_N");
        TryPlaceBreakableWall(cfg.south, breakableWallSouth, centre + new Vector3(0, 0, -half), parent, "HiddenWall_S");
        TryPlaceBreakableWall(cfg.east,  breakableWallEast,  centre + new Vector3( half, 0, 0), parent, "HiddenWall_E");
        TryPlaceBreakableWall(cfg.west,  breakableWallWest,  centre + new Vector3(-half, 0, 0), parent, "HiddenWall_W");
    }

    private void TryPlaceBreakableWall(
        ProceduralDungeonGenerator.EdgeType edge,
        GameObject prefab,
        Vector3 pos,
        GameObject parent,
        string label)
    {
        if (edge != ProceduralDungeonGenerator.EdgeType.Open) return;
        if (prefab == null)
        {
            Debug.LogWarning($"[HiddenRoomSetup] No prefab assigned for edge needed by '{label}'.");
            return;
        }

        // Use the prefab's own rotation (baked into the asset), same as SpawnRoomSetup
        GameObject wall = Instantiate(prefab, pos, prefab.transform.rotation, parent.transform);
        wall.name = $"BreakableWall_{label}";
        spawnedObjects.Add(wall);
    }

    // ── Hidden code number ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by ProceduralDungeonGenerator AFTER CodeNumberManager.InitializeForLevel
    /// so that the digit values are already generated before we spawn the number in the room.
    /// </summary>
    public void SpawnNumberAfterInit(int levelIndex, float tileSize)
    {
        if (pendingHiddenPos.x < 0)
        {
            Debug.LogWarning($"[HiddenRoomSetup] SpawnNumberAfterInit(L{levelIndex}): pendingHiddenPos is invalid — SetupLevel may have failed or not run.");
            return;
        }
        if (pendingGen == null)
        {
            Debug.LogWarning($"[HiddenRoomSetup] SpawnNumberAfterInit(L{levelIndex}): pendingGen is null.");
            return;
        }

        Debug.Log($"[HiddenRoomSetup] SpawnNumberAfterInit(L{levelIndex}): spawning number at grid {pendingHiddenPos}.");
        SpawnHiddenNumber(pendingGen, pendingHiddenPos, levelIndex, tileSize);
        pendingHiddenPos = new Vector2Int(-1, -1);
        pendingGen = null;
    }

    private void SpawnHiddenNumber(
        ProceduralDungeonGenerator gen,
        Vector2Int pos,
        int levelIndex,
        float tileSize)
    {
        if (CodeNumberManager.Instance == null)
        {
            Debug.LogError("[HiddenRoomSetup] SpawnHiddenNumber: CodeNumberManager.Instance is null.");
            return;
        }

        GameObject tile = gen.GetPlacedTile(pos.x, pos.y);
        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);

        if (tile == null)
        {
            Debug.LogError($"[HiddenRoomSetup] SpawnHiddenNumber: GetPlacedTile({pos.x},{pos.y}) returned null for L{levelIndex}.");
            return;
        }
        if (cfg == null)
        {
            Debug.LogError($"[HiddenRoomSetup] SpawnHiddenNumber: GetTileConfig({pos.x},{pos.y}) returned null for L{levelIndex}.");
            return;
        }

        Debug.Log($"[HiddenRoomSetup] SpawnHiddenNumber: calling SpawnHiddenRoomNumber for L{levelIndex} at tile {pos}. " +
                  $"Edges — N:{cfg.north} S:{cfg.south} E:{cfg.east} W:{cfg.west}");

        GameObject spawnedNumber = CodeNumberManager.Instance.SpawnHiddenRoomNumber(tile, cfg, tileSize, levelIndex);
        if (spawnedNumber != null)
        {
            spawnedObjects.Add(spawnedNumber);
            Debug.Log($"[HiddenRoomSetup] Hidden number spawned successfully for L{levelIndex}.");
        }
        else
        {
            Debug.LogError($"[HiddenRoomSetup] SpawnHiddenRoomNumber returned null for L{levelIndex} — check CodeNumberManager logs.");
        }
    }

    // ── Goggles pickup ─────────────────────────────────────────────────────────

    private void SpawnGoggles(
        ProceduralDungeonGenerator gen,
        List<Vector2Int> reachable,
        Vector2Int hiddenPos,
        Vector3 spawnRef,
        GameObject parent,
        float tileSize,
        float levelY,
        int levelIndex)
    {
        if (gogglePickupPrefab == null) return;

        // Prefer the powerbox tile so the player finds both together
        if (powerboxTile.x >= 0)
        {
            GameObject pbTileGO = gen.GetPlacedTile(powerboxTile.x, powerboxTile.y);
            float floorY = pbTileGO != null ? pbTileGO.transform.position.y + 0.5f : levelY + 0.5f;
            Vector3 spawnPos = new Vector3(powerboxTile.x * tileSize, floorY, powerboxTile.y * tileSize);
            GameObject pickup = Instantiate(gogglePickupPrefab, spawnPos, Quaternion.identity, parent.transform);
            pickup.name = $"GogglesPickup_L{levelIndex}";
            spawnedObjects.Add(pickup);
            return;
        }

        // Fallback: any valid reachable tile (used if no powerbox is set up)
        foreach (Vector2Int pos in reachable)
        {
            if (pos == hiddenPos) continue;

            Vector3 world = new Vector3(pos.x * tileSize, 0f, pos.y * tileSize);
            if (Vector3.Distance(world, spawnRef) < minGoggleSpawnDistance) continue;

            ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
            if (cfg == null) continue;

            GameObject tile   = gen.GetPlacedTile(pos.x, pos.y);
            float      floorY = tile != null ? tile.transform.position.y + 0.5f : levelY + 0.5f;

            Vector3 spawnPos = new Vector3(pos.x * tileSize, floorY, pos.y * tileSize);
            GameObject pickup = Instantiate(gogglePickupPrefab, spawnPos, Quaternion.identity, parent.transform);
            pickup.name = $"GogglesPickup_L{levelIndex}";
            spawnedObjects.Add(pickup);
            return;
        }
    }

    // ── Powerbox ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a perimeter-adjacent, non-corner room tile for the powerbox.
    /// Mirrors ComputerRoomSetup.FindComputerRoomTile but with simpler requirements.
    /// </summary>
    private Vector2Int FindPowerboxTile(
        ProceduralDungeonGenerator gen,
        Vector2Int hiddenPos,
        Vector3 spawnRef,
        float tileSize)
    {
        int w = gen.DungeonWidth;
        int h = gen.DungeonHeight;

        // Collect all perimeter-adjacent candidates (one step inward from each edge),
        // excluding the four corners (x=1&&z=1, etc.) which can cause placement issues.
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = 2; x < w - 2; x++)
        {
            candidates.Add(new Vector2Int(x, 1));
            candidates.Add(new Vector2Int(x, h - 2));
        }
        for (int z = 2; z < h - 2; z++)
        {
            candidates.Add(new Vector2Int(1,     z));
            candidates.Add(new Vector2Int(w - 2, z));
        }

        ShuffleList(candidates);

        foreach (Vector2Int pos in candidates)
        {
            if (pos == hiddenPos) continue;

            ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
            if (cfg == null || !cfg.IsRoomTile()) continue;

            // Need an outer Wall edge to mount the powerbox on
            string outer = GetOuterDirection(pos, w, h);
            bool outerIsWall =
                (outer == "north" && cfg.north == ProceduralDungeonGenerator.EdgeType.Wall) ||
                (outer == "south" && cfg.south == ProceduralDungeonGenerator.EdgeType.Wall) ||
                (outer == "east"  && cfg.east  == ProceduralDungeonGenerator.EdgeType.Wall) ||
                (outer == "west"  && cfg.west  == ProceduralDungeonGenerator.EdgeType.Wall);
            if (!outerIsWall) continue;

            // Must be reachable and not too close to spawn
            Vector3 world = new Vector3(pos.x * tileSize, 0f, pos.y * tileSize);
            if (Vector3.Distance(world, spawnRef) < minGoggleSpawnDistance) continue;

            return pos;
        }

        return new Vector2Int(-1, -1);
    }

    /// <summary>
    /// Places the powerbox against the outer perimeter wall of a tile,
    /// using the same ray-to-wall-surface logic as CodeNumberManager.
    /// </summary>
    private void SpawnPowerbox(
        ProceduralDungeonGenerator gen,
        Vector2Int pos,
        GameObject parent,
        float tileSize,
        int levelIndex,
        float levelY)
    {
        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
        GameObject tile = gen.GetPlacedTile(pos.x, pos.y);
        if (cfg == null || tile == null) return;

        int w = gen.DungeonWidth;
        int h = gen.DungeonHeight;
        string outerDir = GetOuterDirection(pos, w, h);
        float halfSize = tileSize * 0.5f;

        // Determine the wall face vectors (outer wall preferred, same as SpawnComputer)
        (Vector3 offset, Vector3 inward, string dir)[] faces =
        {
            (Vector3.forward * halfSize, Vector3.back,    "north"),
            (Vector3.back    * halfSize, Vector3.forward, "south"),
            (Vector3.right   * halfSize, Vector3.left,    "east"),
            (Vector3.left    * halfSize, Vector3.right,   "west"),
        };

        Vector3 chosenOffset = Vector3.zero;
        Vector3 chosenInward = Vector3.forward;
        bool    found        = false;

        // Pass 0: outer wall only. Pass 1: any wall face.
        for (int pass = 0; pass < 2 && !found; pass++)
        {
            foreach (var (offset, inward, dir) in faces)
            {
                ProceduralDungeonGenerator.EdgeType edge =
                    dir == "north" ? cfg.north :
                    dir == "south" ? cfg.south :
                    dir == "east"  ? cfg.east  : cfg.west;

                if (edge != ProceduralDungeonGenerator.EdgeType.Wall) continue;
                bool isOuter = (dir == outerDir);
                if (pass == 0 && !isOuter) continue;
                if (pass == 1 &&  isOuter) continue;

                chosenOffset = offset;
                chosenInward = inward;
                found = true;
                break;
            }
        }

        if (!found) return;

        Vector3 tilePos   = tile.transform.position;
        Vector3 rayOrigin = tilePos + Vector3.up * 1.2f; // slightly below eye-height
        Vector3 spawnPos;

        if (Physics.Raycast(rayOrigin, chosenOffset.normalized, out RaycastHit hit, powerboxWallRaycast))
        {
            spawnPos   = hit.point + chosenInward * powerboxWallOffset;
            spawnPos.y = tilePos.y;
        }
        else
        {
            spawnPos   = tilePos + chosenOffset + chosenInward * powerboxWallOffset;
            spawnPos.y = tilePos.y;
        }

        // Face the powerbox into the room (same orientation as the computer)
        Quaternion rotation = Quaternion.LookRotation(chosenInward, Vector3.up);

        GameObject pb = Instantiate(powerboxPrefab, spawnPos, rotation, parent.transform);
        pb.name = $"Powerbox_L{levelIndex}";
        spawnedObjects.Add(pb);
    }

    private string GetOuterDirection(Vector2Int pos, int w, int h)
    {
        if (pos.x == 1)     return "west";
        if (pos.x == w - 2) return "east";
        if (pos.y == 1)     return "south";
        return "north";
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void ClearAll()
    {
        foreach (GameObject obj in spawnedObjects)
        {
            if (obj == null) continue;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
        spawnedObjects.Clear();

        pendingHiddenPos = new Vector2Int(-1, -1);
        powerboxTile     = new Vector2Int(-1, -1);
        pendingGen       = null;

        // Also wipe excluded tiles since we are tearing down everything
        if (CodeNumberManager.Instance != null)
            CodeNumberManager.Instance.ClearExcludedTiles();
    }

    // ── Utility ────────────────────────────────────────────────────────────────

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }
}
