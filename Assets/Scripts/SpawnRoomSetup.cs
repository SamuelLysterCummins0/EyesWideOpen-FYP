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

    public Vector3 GetSpawnPoint(int levelIndex)
    {
        if (spawnPoints.TryGetValue(levelIndex, out Vector3 point))
            return point;

        Debug.LogWarning($"SpawnRoomSetup: No spawn point registered for level {levelIndex}");
        return Vector3.zero;
    }

    public bool HasSpawnPoint(int levelIndex) => spawnPoints.ContainsKey(levelIndex);

    public void SetupLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent)
    {
        List<Vector2Int> stairsPosList = gen.StairsPositions;

        Vector2Int stairsPos = Vector2Int.zero;
        bool hasStairs = stairsPosList != null && levelIndex < stairsPosList.Count;
        if (hasStairs)
            stairsPos = stairsPosList[levelIndex];

        // The staircase safe room entrance tile — we want to stay away from this
        Vector2Int entranceTile = hasStairs ? GetStepInward(stairsPos, gen.DungeonWidth, gen.DungeonHeight) : new Vector2Int(-1, -1);

        int w = gen.DungeonWidth;
        int h = gen.DungeonHeight;
        float tileSize = gen.TileSize;
        float levelY = levelIndex * -gen.LevelHeight;

        Vector2Int spawnPos = FindSpawnRoomTile(gen, stairsPos, entranceTile, hasStairs, w, h);

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

        TryPlaceDoor(cfg.north, doorNorth, tileCenter, new Vector3(0, 0,  tileSize * 0.5f), levelParent, outerDir == "north");
        TryPlaceDoor(cfg.east,  doorEast,  tileCenter, new Vector3( tileSize * 0.5f, 0, 0), levelParent, outerDir == "east");
        TryPlaceDoor(cfg.south, doorSouth, tileCenter, new Vector3(0, 0, -tileSize * 0.5f), levelParent, outerDir == "south");
        TryPlaceDoor(cfg.west,  doorWest,  tileCenter, new Vector3(-tileSize * 0.5f, 0, 0), levelParent, outerDir == "west");

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
        bool hasStairs, int w, int h)
    {
        string stairsEdge = hasStairs ? GetOuterDirection(stairsPos, w, h) : "none";
        List<(Vector2Int pos, int priority)> candidates = new List<(Vector2Int, int)>();

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

            // Must not be the staircase entrance tile
            if (hasStairs && pos == entranceTile) continue;

            // Must be far enough from the staircase entrance to avoid overlap
            if (hasStairs && entranceTile.x >= 0)
            {
                int dist = Mathf.Abs(pos.x - entranceTile.x) + Mathf.Abs(pos.y - entranceTile.y);
                if (dist < minDistanceFromStairs) continue;
            }

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
        bool skip = false)
    {
        if (skip) return;
        if (edge == ProceduralDungeonGenerator.EdgeType.Wall) return;
        if (prefab == null)
        {
            Debug.LogWarning("SpawnRoomSetup: Door prefab not assigned for an open side.");
            return;
        }

        Vector3 spawnPos = tileCenter + offset;
        GameObject door = Instantiate(prefab, spawnPos, prefab.transform.rotation, parent.transform);
        door.name = $"SpawnRoomDoor_{parent.name}_{offset.normalized}";
        spawnedDoors.Add(door);
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
