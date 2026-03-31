using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Spawns safe room doors on the entrance tile of each level.
// The entrance tile is the first interior tile one step inward from the stairs —
// the same tile the player steps onto after descending from the level above.
// Called by ProceduralDungeonGenerator after PlaceStairsOnEdge() for each level.
public class SafeRoomSetup : MonoBehaviour
{
    [Header("Door Prefabs (one per cardinal direction, facing outward)")]
    public GameObject doorNorth;  // faces +Z (blocks passage coming from the north)
    public GameObject doorEast;   // faces +X
    public GameObject doorSouth;  // faces -Z
    public GameObject doorWest;   // faces -X

    [Header("Settings")]
    [Tooltip("Height at which to spawn the door pivot. 0 = floor level.")]
    public float doorSpawnY = 0f;

    // All spawned doors across all levels — used for cleanup on regeneration
    private List<GameObject> spawnedDoors = new List<GameObject>();

    // World-space centres of every safe-room entrance tile, used by NPCSpawnManager
    // to keep enemies out of safe rooms.
    private List<Vector3> safeRoomCenters = new List<Vector3>();
    public List<Vector3> GetSafeRoomCenters() => safeRoomCenters;

    public void SetupLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent)
    {
        List<Vector2Int> stairsPositions = gen.StairsPositions;
        if (stairsPositions == null || stairsPositions.Count == 0) return;

        // Every level needs a safe room around its OWN departure stairs
        // (the stairs going DOWN to the next level) — exactly like level 0 does.
        // That uses stairsPositions[levelIndex].
        if (levelIndex < stairsPositions.Count)
            SetupStairsEntrance(stairsPositions[levelIndex], levelIndex, gen, levelParent);

        // Levels above 0 also need a safe room at the ARRIVAL tile — the first tile
        // the player steps on after descending from the level above.
        // That uses stairsPositions[levelIndex - 1].
        if (levelIndex > 0 && (levelIndex - 1) < stairsPositions.Count)
            SetupStairsEntrance(stairsPositions[levelIndex - 1], levelIndex, gen, levelParent);
    }

    // Places safe room doors on the entrance tile one step inward from the given stairs position.
    // Skips the side facing back toward the stairs (it already has the stairway's own opening).
    private void SetupStairsEntrance(Vector2Int stairsPos, int levelIndex,
                                     ProceduralDungeonGenerator gen, GameObject levelParent)
    {
        Vector2Int entrancePos = GetEntranceTile(stairsPos, gen.DungeonWidth, gen.DungeonHeight);

        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(entrancePos.x, entrancePos.y);
        if (cfg == null)
        {
            Debug.LogWarning($"SafeRoomSetup: No tile config at ({entrancePos.x},{entrancePos.y}) for level {levelIndex} stairs at {stairsPos}");
            return;
        }

        float tileSize = gen.TileSize;
        float levelY   = levelIndex * -gen.LevelHeight;
        Vector3 tileCenter = new Vector3(entrancePos.x * tileSize, levelY + doorSpawnY, entrancePos.y * tileSize);

        // Track this entrance centre so NPCSpawnManager can exclude the area.
        safeRoomCenters.Add(tileCenter);

        string skipDir = GetStairsDirection(stairsPos, gen.DungeonWidth, gen.DungeonHeight);

        TryPlaceDoor(cfg.north, doorNorth, tileCenter, new Vector3(0, 0,  tileSize * 0.5f), levelParent, skipDir == "north");
        TryPlaceDoor(cfg.east,  doorEast,  tileCenter, new Vector3( tileSize * 0.5f, 0, 0), levelParent, skipDir == "east");
        TryPlaceDoor(cfg.south, doorSouth, tileCenter, new Vector3(0, 0, -tileSize * 0.5f), levelParent, skipDir == "south");
        TryPlaceDoor(cfg.west,  doorWest,  tileCenter, new Vector3(-tileSize * 0.5f, 0, 0), levelParent, skipDir == "west");

        Debug.Log($"SafeRoomSetup: Doors placed at ({entrancePos.x},{entrancePos.y}) level {levelIndex} (stairs at {stairsPos})");
    }

    private void TryPlaceDoor(
        ProceduralDungeonGenerator.EdgeType edge,
        GameObject prefab,
        Vector3 tileCenter,
        Vector3 offset,
        GameObject parent,
        bool skip = false)
    {
        // Skip the side that faces back toward the stairs — it already has its own entry.
        if (skip) return;
        // Only place doors at genuine wide-open junctions.
        // Skipping Center/Left/Right prevents misaligned doors against corridor openings.
        if (edge != ProceduralDungeonGenerator.EdgeType.Open) return;
        if (prefab == null)
        {
            Debug.LogWarning("SafeRoomSetup: Door prefab not assigned for an open side.");
            return;
        }

        Vector3 spawnPos = tileCenter + offset;
        GameObject door = Instantiate(prefab, spawnPos, prefab.transform.rotation, parent.transform);
        door.name = $"SafeRoomDoor_{parent.name}_{offset.normalized}";
        spawnedDoors.Add(door);
    }

    // Returns which cardinal direction (as a string) faces back toward the stairs from the entrance tile.
    // The entrance tile is one step inward, so the "back" direction is simply toward the perimeter edge
    // the stairs sit on.
    private string GetStairsDirection(Vector2Int stairsPos, int dungeonWidth, int dungeonHeight)
    {
        if (stairsPos.x == 0)                  return "west";
        if (stairsPos.x == dungeonWidth - 1)   return "east";
        if (stairsPos.y == 0)                  return "south";
        return                                         "north";
    }

    // Returns the first interior grid cell one step inward from the stairs position.
    // Mirrors the path-inward logic already used in ProceduralDungeonGenerator (lines 416-442).
    private Vector2Int GetEntranceTile(Vector2Int stairsPos, int dungeonWidth, int dungeonHeight)
    {
        if (stairsPos.x == 0)                      return stairsPos + new Vector2Int(1, 0);
        if (stairsPos.x == dungeonWidth - 1)        return stairsPos + new Vector2Int(-1, 0);
        if (stairsPos.y == 0)                       return stairsPos + new Vector2Int(0, 1);
        /* stairsPos.y == dungeonHeight - 1 */      return stairsPos + new Vector2Int(0, -1);
    }

    // Called from ProceduralDungeonGenerator.ClearDungeon() to remove all safe room doors.
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
        safeRoomCenters.Clear();
    }
}
