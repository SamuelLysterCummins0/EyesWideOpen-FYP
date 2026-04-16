using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds a valid perimeter-adjacent room tile for the computer terminal,
/// places safe-room doors on all its open inward edges, and spawns the
/// computer prefab against the outer wall — exactly mirroring SpawnRoomSetup.
///
/// Called by ProceduralDungeonGenerator after SpawnRoomSetup.SetupLevel().
/// </summary>
public class ComputerRoomSetup : MonoBehaviour
{
    [Header("Door Prefabs (one per cardinal direction, facing outward)")]
    public GameObject doorNorth;
    public GameObject doorEast;
    public GameObject doorSouth;
    public GameObject doorWest;

    [Header("Computer")]
    [Tooltip("The ComputerTerminal prefab — must have ComputerInteraction on the root.")]
    public GameObject computerPrefab;

    [Header("Settings")]
    [Tooltip("Height at which to spawn door pivots.")]
    public float doorSpawnY = 0f;
    [Tooltip("Minimum grid-tile Manhattan distance between the computer room and the staircase entrance tile.")]
    public int minDistanceFromStairs = 4;
    [Tooltip("Minimum grid-tile Manhattan distance between the computer room and the player spawn room tile.")]
    public int minDistanceFromSpawnRoom = 4;
    [Tooltip("Raycast distance used to confirm wall geometry exists.")]
    public float wallRaycastDistance = 2.5f;

    [Header("Computer Position & Rotation Tuning")]
    [Tooltip("Offset applied in the computer's own local space AFTER it is placed against the wall.\n" +
             "  Z = depth from wall surface (positive = further into the room)\n" +
             "  X = slide left/right along the wall (positive = right when facing the screen)\n" +
             "  Y = up/down")]
    public Vector3 computerLocalOffset = new Vector3(0f, 0f, 0.5f);
    [Tooltip("Extra Y-axis rotation added after the screen is aimed into the room.\n" +
             "Set to 180 if the screen faces the wall instead of the room.")]
    public float computerRotationOffset = 0f;

    // Tracked for ClearAll()
    private readonly Dictionary<int, GameObject> spawnedComputers = new Dictionary<int, GameObject>();
    private readonly List<GameObject>            spawnedDoors     = new List<GameObject>();
    // World-space tile centres per level — used by DungeonNavMeshSetup to exclude the room from NPC spawn zones.
    private readonly Dictionary<int, Vector3>    roomCentersByLevel = new Dictionary<int, Vector3>();
    public List<Vector3> GetRoomCenters() => new List<Vector3>(roomCentersByLevel.Values);

    // ── Entry point ───────────────────────────────────────────────────────────

    // spawnRoomSetup is passed in so we can exclude the spawn room tile from candidates
    public void SetupLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent,
                           SpawnRoomSetup spawnRoomSetup = null, int connStartX = 0, int connStartZ = 0)
    {
        if (computerPrefab == null)
        {
            Debug.LogError("[ComputerRoomSetup] computerPrefab not assigned!");
            return;
        }

        List<Vector2Int> stairsPosList = gen.StairsPositions;
        int stairsRef  = levelIndex > 0 ? levelIndex - 1 : levelIndex;
        bool hasStairs = stairsPosList != null && stairsRef < stairsPosList.Count;
        Vector2Int stairsPos    = hasStairs ? stairsPosList[stairsRef] : Vector2Int.zero;
        Vector2Int entranceTile = hasStairs ? GetStepInward(stairsPos, gen.DungeonWidth, gen.DungeonHeight)
                                            : new Vector2Int(-1, -1);

        // Convert the spawn room world position to a grid position so we can exclude it
        Vector2Int spawnRoomGrid = new Vector2Int(-1, -1);
        if (spawnRoomSetup != null && spawnRoomSetup.HasSpawnPoint(levelIndex))
        {
            Vector3 spawnWorld = spawnRoomSetup.GetSpawnPoint(levelIndex);
            spawnRoomGrid = new Vector2Int(
                Mathf.RoundToInt(spawnWorld.x / gen.TileSize),
                Mathf.RoundToInt(spawnWorld.z / gen.TileSize));
        }

        int w = gen.DungeonWidth;
        int h = gen.DungeonHeight;

        Vector2Int roomPos = FindComputerRoomTile(gen, stairsPos, entranceTile, hasStairs, spawnRoomGrid, w, h, connStartX, connStartZ);

        if (roomPos.x < 0)
        {
            Debug.LogWarning("[ComputerRoomSetup] Could not find a valid tile. Try reducing minDistanceFromStairs.");
            return;
        }

        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(roomPos.x, roomPos.y);
        if (cfg == null) return;

        float tileSize   = gen.TileSize;
        float levelY     = levelIndex * -gen.LevelHeight;
        Vector3 tileCenter = new Vector3(roomPos.x * tileSize, levelY + doorSpawnY, roomPos.y * tileSize);
        roomCentersByLevel[levelIndex] = tileCenter;

        string outerDir = GetOuterDirection(roomPos, w, h);

        // Place doors on every Open inward edge, skip the outer wall face.
        // Collect SafeRoomDoor refs so RoomNPCShuffle can gate NPC vision on door state.
        List<SafeRoomDoor> levelDoors = new List<SafeRoomDoor>();
        TryPlaceDoor(cfg.north, doorNorth, tileCenter, new Vector3(0, 0,  tileSize * 0.5f), levelParent, roomPos, gen, outerDir == "north", levelDoors);
        TryPlaceDoor(cfg.east,  doorEast,  tileCenter, new Vector3( tileSize * 0.5f, 0, 0), levelParent, roomPos, gen, outerDir == "east",  levelDoors);
        TryPlaceDoor(cfg.south, doorSouth, tileCenter, new Vector3(0, 0, -tileSize * 0.5f), levelParent, roomPos, gen, outerDir == "south", levelDoors);
        TryPlaceDoor(cfg.west,  doorWest,  tileCenter, new Vector3(-tileSize * 0.5f, 0, 0), levelParent, roomPos, gen, outerDir == "west",  levelDoors);

        CreateRoomNPCShuffle(tileCenter, levelIndex, levelParent, levelDoors);

        // Spawn the computer against the best available wall in this tile
        GameObject tile = gen.GetPlacedTile(roomPos.x, roomPos.y);
        SpawnComputer(tile, outerDir, tileSize, levelIndex, cfg);

        Debug.Log($"[ComputerRoomSetup] Level {levelIndex}: computer room at ({roomPos.x},{roomPos.y}), outer wall facing {outerDir}.");
    }

    // ── Tile Finding — identical validation to SpawnRoomSetup.FindSpawnRoomTile ──

    private Vector2Int FindComputerRoomTile(
        ProceduralDungeonGenerator gen,
        Vector2Int stairsPos, Vector2Int entranceTile,
        bool hasStairs, Vector2Int spawnRoomGrid, int w, int h,
        int connStartX, int connStartZ)
    {
        string stairsEdge = hasStairs ? GetOuterDirection(stairsPos, w, h) : "none";
        List<(Vector2Int pos, int priority)> candidates = new List<(Vector2Int, int)>();

        // Build reachable set once so candidate filtering is O(1) per candidate.
        HashSet<Vector2Int> reachableSet = new HashSet<Vector2Int>(gen.GetReachableTilePositions(connStartX, connStartZ));

        // Scan one step inward from every perimeter edge
        for (int x = 0; x < w; x++)
        {
            AddInwardCandidate(new Vector2Int(x, 1),     "south", stairsEdge, candidates);
            AddInwardCandidate(new Vector2Int(x, h - 2), "north", stairsEdge, candidates);
        }
        for (int z = 1; z < h - 1; z++)
        {
            AddInwardCandidate(new Vector2Int(1,     z), "west", stairsEdge, candidates);
            AddInwardCandidate(new Vector2Int(w - 2, z), "east", stairsEdge, candidates);
        }

        // Best candidate first: opposite edge to stairs → adjacent edges → same edge
        candidates.Sort((a, b) => a.priority.CompareTo(b.priority));

        foreach (var (pos, _) in candidates)
        {
            // Must be a placed room tile
            ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
            if (cfg == null || !cfg.IsRoomTile()) continue;

            // Must be reachable from the dungeon's connectivity anchor
            if (!reachableSet.Contains(pos)) continue;

            // Must not be the staircase entrance tile
            if (hasStairs && pos == entranceTile) continue;

            // Must be far enough from the staircase entrance
            if (hasStairs && entranceTile.x >= 0)
            {
                int dist = Mathf.Abs(pos.x - entranceTile.x) + Mathf.Abs(pos.y - entranceTile.y);
                if (dist < minDistanceFromStairs) continue;
            }

            // Must not be the spawn room tile, and must be far enough away from it
            if (spawnRoomGrid.x >= 0)
            {
                if (pos == spawnRoomGrid) continue;
                int distFromSpawn = Mathf.Abs(pos.x - spawnRoomGrid.x) + Mathf.Abs(pos.y - spawnRoomGrid.y);
                if (distFromSpawn < minDistanceFromSpawnRoom) continue;
            }

            // Outer-facing edge must be Wall
            string outerDir = GetOuterDirection(pos, w, h);
            if (outerDir == "west"  && cfg.west  != ProceduralDungeonGenerator.EdgeType.Wall) continue;
            if (outerDir == "east"  && cfg.east  != ProceduralDungeonGenerator.EdgeType.Wall) continue;
            if (outerDir == "south" && cfg.south != ProceduralDungeonGenerator.EdgeType.Wall) continue;
            if (outerDir == "north" && cfg.north != ProceduralDungeonGenerator.EdgeType.Wall) continue;

            // Reject corner-adjacent positions — they face two perimeter edges
            bool isCorner = (pos.x == 1     && pos.y == 1    ) ||
                            (pos.x == 1     && pos.y == h - 2) ||
                            (pos.x == w - 2 && pos.y == 1    ) ||
                            (pos.x == w - 2 && pos.y == h - 2);
            if (isCorner) continue;

            // Require at least one inward Open edge whose neighbour is:
            //   (a) a real placed tile (not fill),
            //   (b) reachable from the dungeon connectivity anchor, and
            //   (c) not a Left/Right partial-wall on its reciprocal face.
            // Condition (b) prevents a room where every door opens onto a dead-end
            // tile the player can never reach. It is fine for SOME doors to fail —
            // only ALL doors failing (no valid exit at all) rejects the candidate.
            bool hasAtLeastOneValidDoor = false;
            if (outerDir != "north" && cfg.north == ProceduralDungeonGenerator.EdgeType.Open)
            {
                Vector2Int adjPos = new Vector2Int(pos.x, pos.y + 1);
                var adj = gen.GetTileConfig(adjPos.x, adjPos.y);
                if (adj != null && adj.tileName != "Tiles_01_Fill"
                    && reachableSet.Contains(adjPos)
                    && adj.south != ProceduralDungeonGenerator.EdgeType.Left
                    && adj.south != ProceduralDungeonGenerator.EdgeType.Right)
                    hasAtLeastOneValidDoor = true;
            }
            if (outerDir != "south" && cfg.south == ProceduralDungeonGenerator.EdgeType.Open)
            {
                Vector2Int adjPos = new Vector2Int(pos.x, pos.y - 1);
                var adj = gen.GetTileConfig(adjPos.x, adjPos.y);
                if (adj != null && adj.tileName != "Tiles_01_Fill"
                    && reachableSet.Contains(adjPos)
                    && adj.north != ProceduralDungeonGenerator.EdgeType.Left
                    && adj.north != ProceduralDungeonGenerator.EdgeType.Right)
                    hasAtLeastOneValidDoor = true;
            }
            if (outerDir != "east" && cfg.east == ProceduralDungeonGenerator.EdgeType.Open)
            {
                Vector2Int adjPos = new Vector2Int(pos.x + 1, pos.y);
                var adj = gen.GetTileConfig(adjPos.x, adjPos.y);
                if (adj != null && adj.tileName != "Tiles_01_Fill"
                    && reachableSet.Contains(adjPos)
                    && adj.west != ProceduralDungeonGenerator.EdgeType.Left
                    && adj.west != ProceduralDungeonGenerator.EdgeType.Right)
                    hasAtLeastOneValidDoor = true;
            }
            if (outerDir != "west" && cfg.west == ProceduralDungeonGenerator.EdgeType.Open)
            {
                Vector2Int adjPos = new Vector2Int(pos.x - 1, pos.y);
                var adj = gen.GetTileConfig(adjPos.x, adjPos.y);
                if (adj != null && adj.tileName != "Tiles_01_Fill"
                    && reachableSet.Contains(adjPos)
                    && adj.east != ProceduralDungeonGenerator.EdgeType.Left
                    && adj.east != ProceduralDungeonGenerator.EdgeType.Right)
                    hasAtLeastOneValidDoor = true;
            }
            if (!hasAtLeastOneValidDoor) continue;

            return pos;
        }

        return new Vector2Int(-1, -1);
    }

    // ── Computer Spawning ─────────────────────────────────────────────────────
    // Mirrors CodeNumberManager.FindValidWallSurface exactly, adapted for floor-level placement.
    // Priority: outer perimeter wall first (the "back wall" facing the room entrance),
    // falling back to any interior Wall-type face if the outer wall is unavailable.

    private void SpawnComputer(GameObject tile, string outerDir, float tileSize, int levelIndex,
                               ProceduralDungeonGenerator.TileConfig cfg)
    {
        if (tile == null)
        {
            Debug.LogWarning("[ComputerRoomSetup] Tile GameObject is null — skipping spawn.");
            return;
        }

        float halfSize = tileSize * 0.5f;

        // Identical face layout to CodeNumberManager.FindValidWallSurface
        (Vector3 offset, Vector3 inwardNormal, ProceduralDungeonGenerator.EdgeType edge, string dir)[] faces =
        {
            (Vector3.forward * halfSize, Vector3.back,    cfg.north, "north"),
            (Vector3.back    * halfSize, Vector3.forward, cfg.south, "south"),
            (Vector3.right   * halfSize, Vector3.left,    cfg.east,  "east"),
            (Vector3.left    * halfSize, Vector3.right,   cfg.west,  "west"),
        };

        // Shuffle so we don't always use the same face when there are multiple options
        for (int i = faces.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = faces[i]; faces[i] = faces[j]; faces[j] = tmp;
        }

        // Pass 1: prefer the outer perimeter wall — this is the "back wall" of the room,
        //         opposite the door the player walks in through.
        // Pass 2: fall back to any interior Wall face (skip Open/door edges).
        Vector3 chosenOffset = Vector3.zero;
        Vector3 chosenInward = Vector3.forward;
        bool found = false;

        for (int pass = 0; pass < 2 && !found; pass++)
        {
            foreach (var (offset, inwardNormal, edge, dir) in faces)
            {
                if (edge != ProceduralDungeonGenerator.EdgeType.Wall) continue;
                bool isOuter = (dir == outerDir);
                if (pass == 0 && !isOuter) continue; // pass 0 = outer wall only
                if (pass == 1 &&  isOuter) continue; // pass 1 = interior walls only

                chosenOffset = offset;
                chosenInward = inwardNormal;
                found = true;
                break;
            }
        }

        if (!found)
        {
            Debug.LogWarning("[ComputerRoomSetup] No valid wall face found — skipping spawn.");
            return;
        }

        // Raycast from tile centre at eye height toward the wall — identical to CodeNumberManager
        Vector3 tilePos   = tile.transform.position;
        Vector3 rayOrigin = tilePos + Vector3.up * 1.5f;
        Vector3 rayDir    = chosenOffset.normalized;
        Vector3 spawnPos;

        // Place the computer right at the wall surface (same as CodeNumberManager's 0.12 f pull-off)
        if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, wallRaycastDistance))
        {
            spawnPos   = hit.point + chosenInward * 0.12f;
            spawnPos.y = tilePos.y;
        }
        else
        {
            Vector3 wallFaceCenter = tilePos + chosenOffset;
            spawnPos   = wallFaceCenter + chosenInward * 0.12f;
            spawnPos.y = tilePos.y;
        }

        // Base rotation: screen faces into the room.
        // computerRotationOffset lets you correct for prefab orientation in the Inspector
        // (e.g. set 180 if the screen ends up facing the wall instead of the room).
        Quaternion rotation = Quaternion.LookRotation(chosenInward, Vector3.up)
                              * Quaternion.Euler(0f, computerRotationOffset, 0f);

        GameObject obj = Instantiate(computerPrefab, spawnPos, rotation);
        obj.name = $"ComputerTerminal_L{levelIndex}";

        // Apply the local-space offset so every axis moves relative to the computer itself,
        // not world axes — this is consistent no matter which wall the computer is on.
        // Z pushes it away from the wall, X slides it left/right along the wall, Y moves it up/down.
        obj.transform.position += obj.transform.right   * computerLocalOffset.x
                                 + obj.transform.up     * computerLocalOffset.y
                                 + obj.transform.forward * computerLocalOffset.z;

        // Tell the ComputerInteraction which level this terminal belongs to so the
        // MazeMinigame can report the correct digit slot to CodeNumberManager.
        ComputerInteraction ci = obj.GetComponentInChildren<ComputerInteraction>(true);
        if (ci == null) ci = obj.GetComponent<ComputerInteraction>();
        if (ci != null) ci.levelIndex = levelIndex;

        if (spawnedComputers.TryGetValue(levelIndex, out GameObject old) && old != null)
            Destroy(old);

        spawnedComputers[levelIndex] = obj;

        Debug.Log($"[ComputerRoomSetup] Spawned computer at {spawnPos}, facing {chosenInward} (level {levelIndex}).");
    }

    // ── Door Placement — identical to SpawnRoomSetup.TryPlaceDoor ────────────

    private void TryPlaceDoor(
        ProceduralDungeonGenerator.EdgeType edge,
        GameObject prefab,
        Vector3 tileCenter,
        Vector3 offset,
        GameObject parent,
        Vector2Int tileGridPos,
        ProceduralDungeonGenerator gen,
        bool skip = false,
        List<SafeRoomDoor> doorList = null)
    {
        if (skip) return;
        if (edge != ProceduralDungeonGenerator.EdgeType.Open) return;

        // Verify a tile exists on the other side — prevents placing doors at the dungeon boundary.
        int nx = tileGridPos.x + (offset.x > 0 ? 1 : offset.x < 0 ? -1 : 0);
        int nz = tileGridPos.y + (offset.z > 0 ? 1 : offset.z < 0 ? -1 : 0);
        ProceduralDungeonGenerator.TileConfig neighborCfg = gen.GetTileConfig(nx, nz);
        if (neighborCfg == null) return;

        // For non-Fill tiles the Wall config value maps to a real physical wall barrier,
        // so only place a door when the neighbour's reciprocal edge is passable.
        // Fill tiles are "open floor, no walls anywhere" (see CanWalkBetween) — their edge
        // configs are meaningless, so we skip the reciprocal check for them.
        if (neighborCfg.tileName != "Tiles_01_Fill")
        {
            ProceduralDungeonGenerator.EdgeType reciprocal =
                offset.z > 0 ? neighborCfg.south :
                offset.z < 0 ? neighborCfg.north :
                offset.x > 0 ? neighborCfg.west  : neighborCfg.east;
            if (reciprocal == ProceduralDungeonGenerator.EdgeType.Wall) return;
        }

        if (prefab == null)
        {
            Debug.LogWarning("[ComputerRoomSetup] Door prefab not assigned for an open side.");
            return;
        }

        GameObject door = Instantiate(prefab, tileCenter + offset, prefab.transform.rotation, parent.transform);
        door.name = $"ComputerRoomDoor_{offset.normalized}";
        spawnedDoors.Add(door);

        SafeRoomDoor safeRoomDoor = door.GetComponent<SafeRoomDoor>();
        if (safeRoomDoor != null && doorList != null)
            doorList.Add(safeRoomDoor);
    }

    // Creates a RoomNPCShuffle on a helper object so closed-door protection is tracked
    // for this room — same pattern as SpawnRoomSetup.CreateRoomNPCShuffle.
    private void CreateRoomNPCShuffle(Vector3 center, int levelIndex, GameObject parent, List<SafeRoomDoor> doors)
    {
        GameObject shuffleObj = new GameObject($"RoomNPCShuffle_ComputerRoom_Level{levelIndex}_{center}");
        shuffleObj.transform.SetParent(parent.transform);
        shuffleObj.transform.position = center;

        RoomNPCShuffle shuffle = shuffleObj.AddComponent<RoomNPCShuffle>();
        shuffle.Initialise(center, levelIndex, doors);

        spawnedDoors.Add(shuffleObj);
    }

    // ── Helpers — identical to SpawnRoomSetup ────────────────────────────────

    private void AddInwardCandidate(Vector2Int pos, string thisEdge, string stairsEdge,
                                    List<(Vector2Int, int)> list)
    {
        string opposite = GetOppositeEdge(stairsEdge);
        int priority;
        if      (thisEdge == opposite)       priority = 0;
        else if (thisEdge != stairsEdge)     priority = 1;
        else                                 priority = 2;
        list.Add((pos, priority));
    }

    private string GetOppositeEdge(string edge)
    {
        switch (edge)
        {
            case "north": return "south";
            case "south": return "north";
            case "east":  return "west";
            case "west":  return "east";
            default:      return "south";
        }
    }

    private string GetOuterDirection(Vector2Int pos, int w, int h)
    {
        if (pos.x == 1)     return "west";
        if (pos.x == w - 2) return "east";
        if (pos.y == 1)     return "south";
        return "north";
    }

    private Vector2Int GetStepInward(Vector2Int perimeterPos, int w, int h)
    {
        if (perimeterPos.x == 0)     return perimeterPos + new Vector2Int(1,  0);
        if (perimeterPos.x == w - 1) return perimeterPos + new Vector2Int(-1, 0);
        if (perimeterPos.y == 0)     return perimeterPos + new Vector2Int(0,  1);
        return                              perimeterPos + new Vector2Int(0, -1);
    }

    // ── Respawn support ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the ComputerInteraction for the given level so GameManager can reset it on respawn.
    /// </summary>
    public ComputerInteraction GetComputerInteractionForLevel(int levelIndex)
    {
        if (!spawnedComputers.TryGetValue(levelIndex, out GameObject obj) || obj == null)
            return null;
        ComputerInteraction ci = obj.GetComponentInChildren<ComputerInteraction>(true);
        return ci != null ? ci : obj.GetComponent<ComputerInteraction>();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void ClearAll()
    {
        foreach (var kvp in spawnedComputers)
        {
            if (kvp.Value == null) continue;
            if (Application.isPlaying) Destroy(kvp.Value);
            else DestroyImmediate(kvp.Value);
        }
        spawnedComputers.Clear();

        foreach (GameObject door in spawnedDoors)
        {
            if (door == null) continue;
            if (Application.isPlaying) Destroy(door);
            else DestroyImmediate(door);
        }
        spawnedDoors.Clear();
        roomCentersByLevel.Clear();
    }
}
