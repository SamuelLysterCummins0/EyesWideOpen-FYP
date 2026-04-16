using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters battery pickups across each dungeon level and places extra items
/// (dead bodies, flashlight, notes, etc.) in the spawn room / stairway entrance tile.
///
/// Extra items have two placement modes:
///
///   AgainstWall  — raycasts to find the solid back wall of the spawn room (no door on
///                  that face) and places the object against it, automatically deriving
///                  the correct world rotation from the wall normal. This is consistent
///                  no matter how the dungeon is generated. All AgainstWall entries for
///                  the same level share the same wall, so a dead body and the item next
///                  to it always appear together.
///
///   AtSpawnPoint — places the object at the spawn point centre with a manual offset.
///                  Use this only for items whose exact position does not matter (e.g.
///                  the flashlight pickup that the player has to find anyway).
///
/// Setup:
///   1. Add to the same GameObject as ProceduralDungeonGenerator.
///   2. Assign batteryPrefab, set batteriesPerLevel.
///   3. Expand Extra Items and add one entry per item.
///      - Dead body:   mode = AgainstWall, wallDepthOffset ~0.15, rotationOffsetX/Y to suit prefab.
///      - Item nearby: mode = AgainstWall, same level, wallSideOffset ~0.6 to slide it along the wall.
///      - Flashlight:  mode = AtSpawnPoint, levelIndex = 1.
/// </summary>
public class BatterySpawnSetup : MonoBehaviour
{
    // ── Placement mode ─────────────────────────────────────────────────────────

    public enum PlacementMode
    {
        [Tooltip("Placed against the solid back wall of the spawn room — correct rotation " +
                 "derived automatically. Consistent across every dungeon generation.")]
        AgainstWall,

        [Tooltip("Placed at the spawn point centre with a manual world-space offset.")]
        AtSpawnPoint,
    }

    // ── Extra item entry ──────────────────────────────────────────────────────

    [System.Serializable]
    public class ExtraItemEntry
    {
        [Tooltip("Prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("Dungeon level (0 = first, 1 = second, etc.).")]
        public int levelIndex = 0;

        [Tooltip("AgainstWall: auto-placed against the back wall.\n" +
                 "AtSpawnPoint: placed at spawn centre with a manual offset.")]
        public PlacementMode placementMode = PlacementMode.AgainstWall;

        [Header("Against Wall settings")]
        [Tooltip("Gap between the wall surface and the object (push it away from the wall into the room).")]
        public float wallDepthOffset = 0.15f;

        [Tooltip("Slide the object along the wall. Positive / negative to move either direction.\n" +
                 "Use this to offset an item sideways from a body on the same wall.")]
        public float wallSideOffset = 0f;

        [Tooltip("Height above the floor.")]
        public float floorHeightOffset = 0.05f;

        [Tooltip("X (pitch) rotation offset applied after the object is faced into the room.")]
        public float rotationOffsetX = 0f;

        [Tooltip("Y (yaw) rotation offset applied after the object is faced into the room.")]
        public float rotationOffsetY = 0f;

        [Header("At Spawn Point settings")]
        [Tooltip("World-space offset from the spawn point centre. Ignored in AgainstWall mode.")]
        public Vector3 spawnPointOffset = Vector3.zero;

        [Tooltip("Spawn facing a random direction. Ignored in AgainstWall mode.")]
        public bool randomYRotation = false;
    }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Batteries")]
    [Tooltip("Battery pickup prefab — must have a BatteryPickup component.")]
    public GameObject batteryPrefab;

    [Tooltip("Batteries per level (index = level number). Last value reused for deeper levels.")]
    public int[] batteriesPerLevel = { 2, 3, 4 };

    [Tooltip("Minimum world-space distance between any two batteries.")]
    public float minSpacing = 8f;

    [Tooltip("Minimum distance from protected room centres (spawn, safe, hidden, computer).")]
    public float minDistanceFromRooms = 6f;

    [Tooltip("Height above the floor for battery placement.")]
    public float floorHeightOffset = 0.1f;

    [Header("Extra Items")]
    [Tooltip("Items placed in the level spawn room / stairway entrance (bodies, flashlight, etc.).\n" +
             "AgainstWall entries for the same level always share the same wall.")]
    public List<ExtraItemEntry> extraItems = new List<ExtraItemEntry>();

    // ── Runtime tracking ──────────────────────────────────────────────────────

    private readonly List<GameObject>                              spawnedObjects  = new List<GameObject>();
    private readonly List<Vector3>                                 placedPositions = new List<Vector3>();

    // Cached wall data per level — shared by all AgainstWall entries on the same level
    // so a body and its item always land against the same wall face.
    private readonly Dictionary<int, (Vector3 wallPos, Vector3 inward)> cachedWalls =
        new Dictionary<int, (Vector3, Vector3)>();

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>Called by ProceduralDungeonGenerator after LockerSetup for each level.</summary>
    public void SetupLevel(
        ProceduralDungeonGenerator gen,
        int                        levelIndex,
        GameObject                 levelParent,
        SpawnRoomSetup             spawnRoomSetup    = null,
        SafeRoomSetup              safeRoomSetup     = null,
        HiddenRoomSetup            hiddenRoomSetup   = null,
        ComputerRoomSetup          computerRoomSetup = null)
    {
        float tileSize = gen.TileSize;
        float levelY   = levelIndex * -gen.LevelHeight;
        int   w        = gen.DungeonWidth;
        int   h        = gen.DungeonHeight;

        // ── Extra items ────────────────────────────────────────────────────────
        if (extraItems != null && spawnRoomSetup != null && spawnRoomSetup.HasSpawnPoint(levelIndex))
        {
            Vector3 sp    = spawnRoomSetup.GetSpawnPoint(levelIndex);
            int     tileX = Mathf.RoundToInt(sp.x / tileSize);
            int     tileZ = Mathf.RoundToInt(sp.z / tileSize);

            foreach (ExtraItemEntry entry in extraItems)
            {
                if (entry.prefab == null || entry.levelIndex != levelIndex) continue;

                GameObject obj = SpawnExtraItem(
                    entry, gen, levelParent,
                    tileX, tileZ, sp, levelY, tileSize,
                    levelIndex, w, h);

                if (obj != null) spawnedObjects.Add(obj);
            }
        }

        // ── Batteries ─────────────────────────────────────────────────────────
        int targetCount = batteriesPerLevel.Length > 0
            ? batteriesPerLevel[Mathf.Clamp(levelIndex, 0, batteriesPerLevel.Length - 1)]
            : 0;

        if (batteryPrefab == null || targetCount <= 0) return;

        List<Vector3> protectedCenters = new List<Vector3>();
        AddRange(protectedCenters, spawnRoomSetup?.GetSpawnRoomPositions());
        AddRange(protectedCenters, safeRoomSetup?.GetSafeRoomCenters());
        AddRange(protectedCenters, hiddenRoomSetup?.GetRoomCenters());
        AddRange(protectedCenters, computerRoomSetup?.GetRoomCenters());

        List<Vector2Int> reachable = gen.GetReachableTilePositions(w / 2, h / 2);
        Shuffle(reachable);

        int placed = 0;
        foreach (Vector2Int tile in reachable)
        {
            if (placed >= targetCount) break;

            Vector3 worldPos = new Vector3(
                tile.x * tileSize, levelY + floorHeightOffset, tile.y * tileSize);

            if (IsNear(worldPos, protectedCenters, minDistanceFromRooms)) continue;
            if (IsNear(worldPos, placedPositions,  minSpacing))           continue;

            GameObject bat = Instantiate(
                batteryPrefab, worldPos, Quaternion.identity, levelParent.transform);
            bat.name = $"Battery_L{levelIndex}_{placed + 1}";
            spawnedObjects.Add(bat);
            placedPositions.Add(worldPos);
            placed++;
        }

        if (placed < targetCount)
            Debug.LogWarning($"[BatterySpawnSetup] L{levelIndex}: placed {placed}/{targetCount} batteries. " +
                             "Try reducing minSpacing or minDistanceFromRooms.");
        else
            Debug.Log($"[BatterySpawnSetup] L{levelIndex}: placed {placed} batteries.");
    }

    // ── Extra item spawning ────────────────────────────────────────────────────

    private GameObject SpawnExtraItem(
        ExtraItemEntry             entry,
        ProceduralDungeonGenerator gen,
        GameObject                 levelParent,
        int tileX, int tileZ,
        Vector3 spawnPoint,
        float levelY, float tileSize,
        int levelIndex, int dungeonWidth, int dungeonHeight)
    {
        Vector3    spawnPos;
        Quaternion rot;

        if (entry.placementMode == PlacementMode.AgainstWall)
        {
            // Resolve and cache the wall for this level on the first AgainstWall entry,
            // then reuse for every subsequent entry so all items share the same wall face.
            if (!cachedWalls.TryGetValue(levelIndex, out var wallData))
            {
                Vector3 tileCenter = new Vector3(tileX * tileSize, levelY, tileZ * tileSize);

                if (!TryFindWall(gen, tileCenter, tileX, tileZ, tileSize,
                                 dungeonWidth, dungeonHeight,
                                 out Vector3 wallSurface, out Vector3 inward))
                {
                    Debug.LogWarning($"[BatterySpawnSetup] No solid wall found for " +
                                     $"'{entry.prefab.name}' on level {levelIndex}. Skipping.");
                    return null;
                }

                wallData = (wallSurface, inward);
                cachedWalls[levelIndex] = wallData;
            }

            Vector3 wallPos      = wallData.wallPos;
            Vector3 inwardNormal = wallData.inward;

            // Slide direction: perpendicular to the wall normal, horizontal.
            // Consistent regardless of which wall was chosen.
            Vector3 alongWall = Vector3.Cross(inwardNormal, Vector3.up).normalized;

            spawnPos = wallPos
                     + inwardNormal * entry.wallDepthOffset
                     + alongWall    * entry.wallSideOffset
                     + Vector3.up   * entry.floorHeightOffset;

            // Face into the room, then apply prefab-specific X/Y rotation correction.
            rot = Quaternion.LookRotation(inwardNormal)
                  * Quaternion.Euler(entry.rotationOffsetX, entry.rotationOffsetY, 0f);
        }
        else // AtSpawnPoint
        {
            spawnPos = new Vector3(
                spawnPoint.x + entry.spawnPointOffset.x,
                levelY + entry.floorHeightOffset + entry.spawnPointOffset.y,
                spawnPoint.z + entry.spawnPointOffset.z);

            rot = entry.randomYRotation
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : Quaternion.identity;
        }

        GameObject obj = Instantiate(entry.prefab, spawnPos, rot, levelParent.transform);
        obj.name = $"{entry.prefab.name}_L{levelIndex}";
        Debug.Log($"[BatterySpawnSetup] '{obj.name}' placed at {spawnPos} " +
                  $"(level {levelIndex}, {entry.placementMode}).");
        return obj;
    }

    // ── Wall detection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a Wall-type face in the spawn tile to place objects against.
    /// Mirrors ComputerRoomSetup.SpawnComputer: prefers the outer perimeter wall
    /// (always solid, guaranteed no door), falls back to any inner Wall face.
    /// </summary>
    private bool TryFindWall(
        ProceduralDungeonGenerator gen,
        Vector3 tileCenter, int tileX, int tileZ,
        float tileSize, int dungeonWidth, int dungeonHeight,
        out Vector3 wallPos, out Vector3 inwardNormal)
    {
        wallPos      = Vector3.zero;
        inwardNormal = Vector3.forward;

        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(tileX, tileZ);
        if (cfg == null) return false;

        float  halfSize = tileSize * 0.5f;
        string outerDir = GetOuterDirection(tileX, tileZ, dungeonWidth, dungeonHeight);

        // (world offset from tile centre, inward normal, edge config, direction label)
        var faces = new (Vector3 offset, Vector3 inward,
                         ProceduralDungeonGenerator.EdgeType edge, string dir)[]
        {
            (Vector3.forward * halfSize, Vector3.back,    cfg.north, "north"),
            (Vector3.back    * halfSize, Vector3.forward, cfg.south, "south"),
            (Vector3.right   * halfSize, Vector3.left,    cfg.east,  "east"),
            (Vector3.left    * halfSize, Vector3.right,   cfg.west,  "west"),
        };

        // Pass 0: prefer the outer perimeter wall — it is always Wall-type and has no door.
        // Pass 1: fall back to any interior Wall face (e.g. if outer face config is unexpected).
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (offset, inward, edge, dir) in faces)
            {
                if (edge != ProceduralDungeonGenerator.EdgeType.Wall) continue;

                bool isOuter = string.Equals(dir, outerDir);
                if (pass == 0 && !isOuter) continue; // outer wall only
                if (pass == 1 &&  isOuter) continue; // inner walls only

                // Raycast to find exact wall surface geometry (same as ComputerRoomSetup)
                Vector3 rayOrigin = tileCenter + Vector3.up * 0.5f;
                Vector3 rayDir    = offset.normalized;

                Vector3 surface;
                if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, tileSize))
                    surface = new Vector3(hit.point.x, tileCenter.y, hit.point.z);
                else
                    surface = tileCenter + offset;

                wallPos      = surface;
                inwardNormal = inward;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns which cardinal direction faces the dungeon boundary for a perimeter-adjacent tile.
    /// Mirrors SpawnRoomSetup.GetOuterDirection.
    /// </summary>
    private string GetOuterDirection(int tileX, int tileZ, int dungeonWidth, int dungeonHeight)
    {
        if (tileX == 1)                 return "west";
        if (tileX == dungeonWidth - 2)  return "east";
        if (tileZ == 1)                 return "south";
        return                                 "north";
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>Called by ProceduralDungeonGenerator.ClearDungeon before regeneration.</summary>
    public void ClearAll()
    {
        spawnedObjects.Clear();
        placedPositions.Clear();
        cachedWalls.Clear(); // must clear so wall is re-detected on the next generation
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsNear(Vector3 pos, List<Vector3> others, float threshold)
    {
        foreach (Vector3 other in others)
            if (Vector3.Distance(pos, other) < threshold) return true;
        return false;
    }

    private void AddRange(List<Vector3> target, List<Vector3> source)
    {
        if (source != null) target.AddRange(source);
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }
}
