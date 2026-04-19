using System.Collections.Generic;
using UnityEngine;

// Spawns a safe room for player respawning on a perimeter edge of each level.
// Rather than scanning the perimeter directly (which is mostly fill/corridor tiles),
// we scan one step INWARD from each edge — exactly like how SafeRoomSetup finds the
// staircase entrance tile — to find an actual room tile we can place doors around.
// Called by ProceduralDungeonGenerator after SafeRoomSetup.SetupLevel().
public class SpawnRoomSetup : MonoBehaviour
{
    [Header("Door Prefabs (one per cardinal direction, facing outward)")]
    public GameObject doorNorth;
    public GameObject doorEast;
    public GameObject doorSouth;
    public GameObject doorWest;

    [Header("Settings")]
    [Tooltip("Height at which to spawn the door pivot. 0 = floor level.")]
    public float doorSpawnY = 0f;
    [Tooltip("Vertical offset for the player spawn point inside the room (above the tile centre).")]
    public float spawnHeightOffset = 1f;
    [Tooltip("Minimum grid tile distance between the spawn room and the staircase safe room entrance tile.")]
    public int minDistanceFromStairs = 4;

    // World-space spawn point per level index — filled in SetupLevel()
    private Dictionary<int, Vector3> spawnPoints = new Dictionary<int, Vector3>();

    // All spawned doors across all levels — used for cleanup on regeneration
    private List<GameObject> spawnedDoors = new List<GameObject>();

    // Returns all spawn-room world positions so NPCSpawnManager can exclude the areas.
    public List<Vector3> GetSpawnRoomPositions() => new List<Vector3>(spawnPoints.Values);

    public Vector3 GetSpawnPoint(int levelIndex)
    {
        if (spawnPoints.TryGetValue(levelIndex, out Vector3 point))
            return point;

        Debug.LogWarning($"SpawnRoomSetup: No spawn point registered for level {levelIndex}");
        return Vector3.zero;
    }

    public bool HasSpawnPoint(int levelIndex) => spawnPoints.ContainsKey(levelIndex);

    public void SetupLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent,
                           int connStartX = 0, int connStartZ = 0)
    {
        List<Vector2Int> stairsPosList = gen.StairsPositions;

        // For level 0: use stairsPosList[0] (level 0's own stairs).
        // For level 1+: the arrival tile is inward from the PREVIOUS level's stairs,
        // so use stairsPosList[levelIndex - 1].
        int stairsRef = levelIndex > 0 ? levelIndex - 1 : levelIndex;
        Vector2Int stairsPos = Vector2Int.zero;
        bool hasStairs = stairsPosList != null && stairsRef < stairsPosList.Count;
        if (hasStairs)
            stairsPos = stairsPosList[stairsRef];

        int w = gen.DungeonWidth;
        int h = gen.DungeonHeight;
        float tileSize = gen.TileSize;
        float levelY = levelIndex * -gen.LevelHeight;

        // For levels below 0: the spawn point is the stairway entrance tile —
        // the same room SafeRoomSetup already equipped with doors when it ran.
        // No new door placement is needed here; just register the spawn point and
        // drop a checkpoint trigger so arriving on this level saves it as the active respawn.
        if (levelIndex > 0 && hasStairs)
        {
            Vector2Int entrancePos = GetStepInward(stairsPos, w, h);
            Vector3 entranceCenter = new Vector3(entrancePos.x * tileSize, levelY + doorSpawnY, entrancePos.y * tileSize);
            spawnPoints[levelIndex] = entranceCenter + Vector3.up * spawnHeightOffset;

            SpawnCheckpointTrigger(entranceCenter, levelIndex, tileSize, levelParent);

            Debug.Log($"SpawnRoomSetup: Level {levelIndex} spawn point + checkpoint trigger placed at stairway entrance ({entrancePos.x},{entrancePos.y}).");
            return;
        }

        // Level 0: find a dedicated perimeter spawn room far from the stairs.
        Vector2Int entranceTile = hasStairs ? GetStepInward(stairsPos, w, h) : new Vector2Int(-1, -1);

        Vector2Int spawnPos = FindSpawnRoomTile(gen, stairsPos, entranceTile, hasStairs, w, h, connStartX, connStartZ);

        if (spawnPos.x < 0)
        {
            Debug.LogWarning($"SpawnRoomSetup: Could not find a valid spawn room tile for level {levelIndex}");
            return;
        }

        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(spawnPos.x, spawnPos.y);
        if (cfg == null) return;

        Vector3 tileCenter = new Vector3(spawnPos.x * tileSize, levelY + doorSpawnY, spawnPos.y * tileSize);
        spawnPoints[levelIndex] = tileCenter + Vector3.up * spawnHeightOffset;

        // The spawn tile sits one step inward from an edge.
        // The direction back toward that edge is the "outer" face — skip it for door placement.
        string outerDir = GetOuterDirection(spawnPos, w, h);

        List<SafeRoomDoor> levelDoors = new List<SafeRoomDoor>();
        TryPlaceDoor(cfg.north, doorNorth, tileCenter, new Vector3(0, 0,  tileSize * 0.5f), levelParent, spawnPos, gen, outerDir == "north", levelDoors);
        TryPlaceDoor(cfg.east,  doorEast,  tileCenter, new Vector3( tileSize * 0.5f, 0, 0), levelParent, spawnPos, gen, outerDir == "east",  levelDoors);
        TryPlaceDoor(cfg.south, doorSouth, tileCenter, new Vector3(0, 0, -tileSize * 0.5f), levelParent, spawnPos, gen, outerDir == "south", levelDoors);
        TryPlaceDoor(cfg.west,  doorWest,  tileCenter, new Vector3(-tileSize * 0.5f, 0, 0), levelParent, spawnPos, gen, outerDir == "west",  levelDoors);

        CreateRoomNPCShuffle(tileCenter, levelIndex, levelParent, levelDoors);

        // Level 0: place a checkpoint trigger so the player's respawn is locked here
        // when they first arrive from the intro room.
        if (levelIndex == 0)
        {
            SpawnCheckpointTrigger(tileCenter, 0, tileSize, levelParent);
        }

        Debug.Log($"SpawnRoomSetup: Spawn room at ({spawnPos.x},{spawnPos.y}) for level {levelIndex}. Spawn point: {spawnPoints[levelIndex]}");
    }

    // Scan candidates that are ONE STEP INWARD from each edge, ordered by preference:
    // opposite edge to stairs first → adjacent edges → same edge as last resort.
    // Each candidate must:
    //   - be a room tile (IsRoomTile())
    //   - not be the staircase entrance tile
    //   - be at least minDistanceFromStairs grid tiles away from the stairs entrance
    private Vector2Int FindSpawnRoomTile(
        ProceduralDungeonGenerator gen,
        Vector2Int stairsPos, Vector2Int entranceTile,
        bool hasStairs, int w, int h,
        int connStartX, int connStartZ)
    {
        string stairsEdge = hasStairs ? GetOuterDirection(stairsPos, w, h) : "none";
        List<(Vector2Int pos, int priority)> candidates = new List<(Vector2Int, int)>();

        // Build reachable set once so candidate filtering is O(1) per candidate.
        HashSet<Vector2Int> reachableSet = new HashSet<Vector2Int>(gen.GetReachableTilePositions(connStartX, connStartZ));

        // Scan one step inward from each of the four edges.
        // Priority: 0 = opposite, 1 = adjacent, 2 = same as stairs
        for (int x = 0; x < w; x++)
        {
            // South edge (z=0), step inward = z=1
            AddInwardCandidate(new Vector2Int(x, 1), "south", stairsEdge, candidates);
            // North edge (z=h-1), step inward = z=h-2
            AddInwardCandidate(new Vector2Int(x, h - 2), "north", stairsEdge, candidates);
        }
        for (int z = 1; z < h - 1; z++)
        {
            // West edge (x=0), step inward = x=1
            AddInwardCandidate(new Vector2Int(1, z), "west", stairsEdge, candidates);
            // East edge (x=w-1), step inward = x=w-2
            AddInwardCandidate(new Vector2Int(w - 2, z), "east", stairsEdge, candidates);
        }

        // Sort by priority (0 first), then evaluate
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

            // Must be far enough from the staircase entrance to avoid overlap
            if (hasStairs && entranceTile.x >= 0)
            {
                int dist = Mathf.Abs(pos.x - entranceTile.x) + Mathf.Abs(pos.y - entranceTile.y);
                if (dist < minDistanceFromStairs) continue;
            }

            // Outer-facing edge must be Wall — otherwise skipping door placement
            // on that side leaves an open gap with no wall and no door.
            string outerDir = GetOuterDirection(pos, w, h);
            if (outerDir == "west"  && cfg.west  != ProceduralDungeonGenerator.EdgeType.Wall) continue;
            if (outerDir == "east"  && cfg.east  != ProceduralDungeonGenerator.EdgeType.Wall) continue;
            if (outerDir == "south" && cfg.south != ProceduralDungeonGenerator.EdgeType.Wall) continue;
            if (outerDir == "north" && cfg.north != ProceduralDungeonGenerator.EdgeType.Wall) continue;

            // Reject corner-adjacent positions — they touch two perimeter edges but
            // GetOuterDirection only returns one, so the second side would be left open.
            bool isCorner = (pos.x == 1     && pos.y == 1    ) ||
                            (pos.x == 1     && pos.y == h - 2) ||
                            (pos.x == w - 2 && pos.y == 1    ) ||
                            (pos.x == w - 2 && pos.y == h - 2);
            if (isCorner) continue;

            // Every inward Open side must be either doorable (neighbour reciprocal Open/Center
            // → door placed) or naturally sealed (neighbour reciprocal Wall). A Left/Right
            // reciprocal creates a passable partial-wall gap with no door and nothing to
            // seal it — reject the whole candidate if any such gap exists.
            // Also requires at least one actual doorable side (reachable, non-fill neighbour
            // with Open/Center reciprocal) so the room always has at least one exit.
            bool hasAtLeastOneValidDoor = false;
            bool hasBadGap = false;

            ProceduralDungeonGenerator.EdgeType GetRec(ProceduralDungeonGenerator.TileConfig adj, string face)
            {
                if (adj.tileName == "Tiles_01_Fill") return ProceduralDungeonGenerator.EdgeType.Open;
                return face == "south" ? adj.south :
                       face == "north" ? adj.north :
                       face == "west"  ? adj.west  : adj.east;
            }

            void CheckSide(ProceduralDungeonGenerator.EdgeType myEdge, Vector2Int adjPos, string recipFace)
            {
                if (myEdge != ProceduralDungeonGenerator.EdgeType.Open) return;
                ProceduralDungeonGenerator.TileConfig adj = gen.GetTileConfig(adjPos.x, adjPos.y);
                if (adj == null) return;
                ProceduralDungeonGenerator.EdgeType rec = GetRec(adj, recipFace);
                if (rec == ProceduralDungeonGenerator.EdgeType.Left ||
                    rec == ProceduralDungeonGenerator.EdgeType.Right)
                { hasBadGap = true; return; }
                if (rec != ProceduralDungeonGenerator.EdgeType.Wall && reachableSet.Contains(adjPos))
                    hasAtLeastOneValidDoor = true;
            }

            if (outerDir != "north") CheckSide(cfg.north, new Vector2Int(pos.x, pos.y + 1), "south");
            if (outerDir != "south") CheckSide(cfg.south, new Vector2Int(pos.x, pos.y - 1), "north");
            if (outerDir != "east")  CheckSide(cfg.east,  new Vector2Int(pos.x + 1, pos.y), "west");
            if (outerDir != "west")  CheckSide(cfg.west,  new Vector2Int(pos.x - 1, pos.y), "east");

            if (hasBadGap || !hasAtLeastOneValidDoor) continue;

            return pos;
        }

        return new Vector2Int(-1, -1);
    }

    private void AddInwardCandidate(Vector2Int pos, string thisEdge, string stairsEdge, List<(Vector2Int, int)> list)
    {
        int priority;
        string opposite = GetOppositeEdge(stairsEdge);
        if (thisEdge == opposite)       priority = 0;   // best — far side from stairs
        else if (thisEdge != stairsEdge) priority = 1;  // adjacent edges
        else                             priority = 2;  // same edge as stairs — last resort

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
        // Only place doors at genuine wide-open junctions.
        // Skipping Center/Left/Right prevents misaligned doors against corridor openings.
        if (edge != ProceduralDungeonGenerator.EdgeType.Open) return;

        // Verify a tile exists on the other side — prevents placing doors at the dungeon boundary.
        int nx = tileGridPos.x + (offset.x > 0 ? 1 : offset.x < 0 ? -1 : 0);
        int nz = tileGridPos.y + (offset.z > 0 ? 1 : offset.z < 0 ? -1 : 0);
        ProceduralDungeonGenerator.TileConfig neighborCfg = gen.GetTileConfig(nx, nz);
        if (neighborCfg == null) return;

        // For non-Fill tiles the Wall/Left/Right config values map to real physical wall
        // geometry on that face — placing a sliding door against any of those clips or
        // blocks the opening. Only place when the neighbour's reciprocal is fully Open
        // (or Center, which still leaves the middle clear for the door).
        // Fill tiles are "open floor, no walls anywhere" (see CanWalkBetween) — their edge
        // configs are meaningless, so we skip the reciprocal check for them.
        if (neighborCfg.tileName != "Tiles_01_Fill")
        {
            ProceduralDungeonGenerator.EdgeType reciprocal =
                offset.z > 0 ? neighborCfg.south :
                offset.z < 0 ? neighborCfg.north :
                offset.x > 0 ? neighborCfg.west  : neighborCfg.east;
            if (reciprocal == ProceduralDungeonGenerator.EdgeType.Wall  ||
                reciprocal == ProceduralDungeonGenerator.EdgeType.Left  ||
                reciprocal == ProceduralDungeonGenerator.EdgeType.Right) return;
        }

        if (prefab == null)
        {
            Debug.LogWarning("SpawnRoomSetup: Door prefab not assigned for an open side.");
            return;
        }

        Vector3 spawnPos = tileCenter + offset;
        GameObject door = Instantiate(prefab, spawnPos, prefab.transform.rotation, parent.transform);
        door.name = $"SpawnRoomDoor_{parent.name}_{offset.normalized}";
        spawnedDoors.Add(door);

        // Collect SafeRoomDoor reference so RoomNPCShuffle can query IsOpen.
        SafeRoomDoor safeRoomDoor = door.GetComponent<SafeRoomDoor>();
        if (safeRoomDoor != null && doorList != null)
            doorList.Add(safeRoomDoor);
    }

    // Creates a RoomNPCShuffle component on a helper object parented to the level.
    // Tracked in spawnedDoors so ClearAll() destroys it on dungeon regeneration.
    private void CreateRoomNPCShuffle(Vector3 center, int levelIndex, GameObject parent, List<SafeRoomDoor> doors)
    {
        GameObject shuffleObj = new GameObject($"RoomNPCShuffle_Level{levelIndex}");
        shuffleObj.transform.SetParent(parent.transform);
        shuffleObj.transform.position = center;

        RoomNPCShuffle shuffle = shuffleObj.AddComponent<RoomNPCShuffle>();
        shuffle.Initialise(center, levelIndex, doors);

        spawnedDoors.Add(shuffleObj);
    }

    // The inward-step candidate tiles sit one tile in from the edge,
    // so their "outer" direction is toward the edge they came from.
    // x==1 → west face open toward west edge, etc.
    private string GetOuterDirection(Vector2Int pos, int w, int h)
    {
        if (pos.x == 1)     return "west";
        if (pos.x == w - 2) return "east";
        if (pos.y == 1)     return "south";
        return "north";
    }

    // One step inward from a perimeter tile — same logic as SafeRoomSetup/PlaceEntranceTile
    private Vector2Int GetStepInward(Vector2Int perimeterPos, int w, int h)
    {
        if (perimeterPos.x == 0)     return perimeterPos + new Vector2Int(1, 0);
        if (perimeterPos.x == w - 1) return perimeterPos + new Vector2Int(-1, 0);
        if (perimeterPos.y == 0)     return perimeterPos + new Vector2Int(0, 1);
        return perimeterPos + new Vector2Int(0, -1);
    }

    // Spawns an invisible trigger at the stairway entrance tile so the player
    // activates the checkpoint simply by walking into the room.
    private void SpawnCheckpointTrigger(Vector3 center, int levelIndex, float tileSize, GameObject parent)
    {
        GameObject triggerObj = new GameObject($"SpawnCheckpoint_Level{levelIndex}");
        triggerObj.transform.position = center;
        triggerObj.transform.SetParent(parent.transform);

        // Box covers most of the tile so the player can't slip through undetected.
        BoxCollider col = triggerObj.AddComponent<BoxCollider>();
        col.size = new Vector3(tileSize * 0.8f, 3f, tileSize * 0.8f);
        col.isTrigger = true;

        SpawnRoomCheckpoint checkpoint = triggerObj.AddComponent<SpawnRoomCheckpoint>();
        checkpoint.Initialise(levelIndex);

        // Level 0 spawn room is the escape destination — player must return here during detonation.
        if (levelIndex == 0)
            triggerObj.AddComponent<WinTrigger>();

        // Track it so ClearAll() can destroy it on dungeon regeneration.
        spawnedDoors.Add(triggerObj);
    }

    public void ClearAll()
    {
        foreach (GameObject door in spawnedDoors)
        {
            if (door == null) continue;
            if (Application.isPlaying)
                Destroy(door);
            else
                DestroyImmediate(door);
        }
        spawnedDoors.Clear();
        spawnPoints.Clear();
    }
}
