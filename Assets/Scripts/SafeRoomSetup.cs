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

    public void SetupLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent)
    {
        List<Vector2Int> stairsPositions = gen.StairsPositions;

        // stairsPositions is populated by PlaceStairsOnEdge — index [levelIndex] is this level's stairs.
        // Level 0 uses index 0, level 1 uses index 1, etc.
        if (stairsPositions == null || levelIndex >= stairsPositions.Count)
        {
            Debug.LogWarning($"SafeRoomSetup: No stairs position for level {levelIndex}");
            return;
        }

        Vector2Int stairsPos = stairsPositions[levelIndex];
        Vector2Int entrancePos = GetEntranceTile(stairsPos, gen.DungeonWidth, gen.DungeonHeight);

        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(entrancePos.x, entrancePos.y);
        if (cfg == null)
        {
            Debug.LogWarning($"SafeRoomSetup: No tile config at entrance tile ({entrancePos.x},{entrancePos.y}) for level {levelIndex}");
            return;
        }

        float tileSize = gen.TileSize;
        float levelY = levelIndex * -gen.LevelHeight;
        Vector3 tileCenter = new Vector3(entrancePos.x * tileSize, levelY + doorSpawnY, entrancePos.y * tileSize);

        // Determine which direction of the entrance tile faces back toward the stairs.
        // That side already has the stairs prefab's own entry/door — skip it.
        string skipDir = GetStairsDirection(stairsPos, gen.DungeonWidth, gen.DungeonHeight);

        // Place a door on every Open side except the one facing back to the stairs.
        TryPlaceDoor(cfg.north, doorNorth, tileCenter, new Vector3(0, 0,  tileSize * 0.5f), levelParent, skipDir == "north");
        TryPlaceDoor(cfg.east,  doorEast,  tileCenter, new Vector3( tileSize * 0.5f, 0, 0), levelParent, skipDir == "east");
        TryPlaceDoor(cfg.south, doorSouth, tileCenter, new Vector3(0, 0, -tileSize * 0.5f), levelParent, skipDir == "south");
        TryPlaceDoor(cfg.west,  doorWest,  tileCenter, new Vector3(-tileSize * 0.5f, 0, 0), levelParent, skipDir == "west");

        Debug.Log($"SafeRoomSetup: Set up safe room at entrance tile ({entrancePos.x},{entrancePos.y}) for level {levelIndex}");
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
        // Only place on passable edges. Wall edges have geometry already.
        // Check all non-Wall types (Open, Center, Left, Right) — any of these mean
        // the player/NPC can walk through and needs a door wall blocking it.
        if (edge == ProceduralDungeonGenerator.EdgeType.Wall) return;
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
    }
}
