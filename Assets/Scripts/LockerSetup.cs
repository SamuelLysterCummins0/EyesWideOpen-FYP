using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns locker hiding spots in dungeon corridors — tiles with exactly 2 open edges
/// that are not inside any protected room area.
///
/// Mirrors the pattern of ComputerRoomSetup: scans reachable tiles, raycasts for wall
/// geometry, instantiates the locker prefab flush against the wall.
///
/// Called by ProceduralDungeonGenerator after HiddenRoomSetup.SetupLevel().
///
/// Setup:
///   1. Add this component to the same GameObject as ProceduralDungeonGenerator.
///   2. Assign lockerPrefab — a prefab with LockerInteraction component.
///   3. Set lockersPerLevel (2–4 recommended).
///   4. Assign in ProceduralDungeonGenerator's Inspector field.
/// </summary>
public class LockerSetup : MonoBehaviour
{
    [Header("Locker Prefab")]
    [Tooltip("The locker prefab. Must have LockerInteraction on the root and a child LockerCamera.")]
    public GameObject lockerPrefab;

    [Header("Spawn Settings")]
    [Tooltip("How many lockers to attempt to place per level.")]
    public int lockersPerLevel = 3;
    [Tooltip("Minimum distance (world units) between any two lockers.")]
    public float minLockerSpacing = 8f;
    [Tooltip("NPC exclusion radius around each locker registered with NPCSpawnManager.")]
    public float lockerExclusionRadius = 3f;
    [Tooltip("Distance at which to confirm wall geometry via raycast.")]
    public float wallRaycastDistance = 2.5f;
    [Tooltip("Minimum distance from any room center to place a locker (world units).")]
    public float minDistanceFromRooms = 5f;
    [Tooltip("Minimum distance from any spawned CodeNumber digit to place a locker (world units).")]
    public float minDistanceFromCodeNumbers = 4f;

    [Header("Position Offset")]
    [Tooltip("Fine-tune the spawned locker position. X = side-to-side, Y = up/down, Z = push from wall.")]
    public Vector3 spawnOffset = Vector3.zero;

    // Tracked per-level for cleanup
    private readonly List<GameObject> spawnedLockers  = new List<GameObject>();
    private readonly List<Vector3>    lockerCenters   = new List<Vector3>();
    private          List<Vector3>    codeNumberPositions = new List<Vector3>();

    public List<Vector3> GetLockerCenters() => new List<Vector3>(lockerCenters);

    // ── Entry Point ────────────────────────────────────────────────────────────

    public void SetupLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent,
                           SpawnRoomSetup    spawnRoomSetup    = null,
                           HiddenRoomSetup   hiddenRoomSetup   = null,
                           ComputerRoomSetup computerRoomSetup = null,
                           SafeRoomSetup     safeRoomSetup     = null,
                           NPCSpawnManager   npcSpawnManager   = null)
    {
        if (lockerPrefab == null)
        {
            Debug.LogWarning("[LockerSetup] lockerPrefab not assigned — skipping locker spawning.");
            return;
        }

        float tileSize  = gen.TileSize;
        int   w         = gen.DungeonWidth;
        int   h         = gen.DungeonHeight;
        float levelY    = levelIndex * -gen.LevelHeight;

        // Collect all room centers to exclude
        List<Vector3> protectedCenters = new List<Vector3>();
        AddCenters(protectedCenters, safeRoomSetup?.GetSafeRoomCenters());
        AddCenters(protectedCenters, spawnRoomSetup?.GetSpawnRoomPositions());
        AddCenters(protectedCenters, hiddenRoomSetup?.GetRoomCenters());
        AddCenters(protectedCenters, computerRoomSetup?.GetRoomCenters());

        // Grab code number positions — CodeNumberManager runs before LockerSetup now
        // so these positions are already populated when we get here
        codeNumberPositions = CodeNumberManager.Instance != null
            ? CodeNumberManager.Instance.GetSpawnedPositions()
            : new List<Vector3>();

        // Get all reachable tiles
        List<Vector2Int> reachable = gen.GetReachableTilePositions(w / 2, h / 2);
        // Shuffle so lockers don't always cluster at the same map area
        Shuffle(reachable);

        int placed = 0;

        foreach (Vector2Int tile in reachable)
        {
            if (placed >= lockersPerLevel) break;

            ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(tile.x, tile.y);
            if (cfg == null) continue;

            // We want corridor tiles: exactly 2 open edges (a straight corridor or L-shape)
            // and NOT a full room tile (which has all 4 edges open/wall)
            int openCount = cfg.GetOpeningCount();
            if (openCount != 2) continue;

            // Must have at least 1 wall edge to place the locker against
            bool hasWall = cfg.north == ProceduralDungeonGenerator.EdgeType.Wall ||
                           cfg.south == ProceduralDungeonGenerator.EdgeType.Wall ||
                           cfg.east  == ProceduralDungeonGenerator.EdgeType.Wall ||
                           cfg.west  == ProceduralDungeonGenerator.EdgeType.Wall;
            if (!hasWall) continue;

            Vector3 tileCenter = new Vector3(tile.x * tileSize, levelY, tile.y * tileSize);

            // Must be far from all protected rooms
            if (IsNearProtectedArea(tileCenter, protectedCenters)) continue;

            // Must not overlap with a code number on the same wall
            if (IsNearCodeNumber(tileCenter)) continue;

            // Must be far from other lockers
            if (IsTooCloseToExistingLockers(tileCenter)) continue;

            // Try to find a wall edge and spawn the locker
            if (TrySpawnLocker(tileCenter, cfg, tileSize, levelParent, npcSpawnManager))
            {
                placed++;
                lockerCenters.Add(tileCenter);
                Debug.Log($"[LockerSetup] Placed locker {placed}/{lockersPerLevel} at tile ({tile.x},{tile.y}) " +
                          $"level {levelIndex}");
            }
        }

        Debug.Log($"[LockerSetup] Level {levelIndex}: placed {placed} lockers.");
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    private bool TrySpawnLocker(Vector3 tileCenter, ProceduralDungeonGenerator.TileConfig cfg,
                                float tileSize, GameObject levelParent, NPCSpawnManager npcSpawnManager)
    {
        // Try each wall direction in priority order
        (Vector3 dir, string side)[] wallDirs =
        {
            (Vector3.forward, "north"), // z+
            (Vector3.back,  "south"), // z-
            (Vector3.right, "east"),  // x+
            (Vector3.left,  "west"),  // x-
        };

        // Map cardinal directions to TileConfig edge types
        foreach (var (dir, side) in wallDirs)
        {
            bool isWall = side == "north" ? cfg.north == ProceduralDungeonGenerator.EdgeType.Wall :
                          side == "south" ? cfg.south == ProceduralDungeonGenerator.EdgeType.Wall :
                          side == "east"  ? cfg.east  == ProceduralDungeonGenerator.EdgeType.Wall :
                                            cfg.west  == ProceduralDungeonGenerator.EdgeType.Wall;
            if (!isWall) continue;

            // Raycast to confirm actual wall geometry exists
            Vector3 rayOrigin = tileCenter + Vector3.up * 1f;
            Vector3 rayDir    = dir;

            if (!Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, wallRaycastDistance, ~(1 << 2)))
                continue; // No wall geometry found

            // Place locker flush against the wall, facing inward (away from wall)
            Vector3 spawnPos = hit.point - rayDir * 0.1f; // Just inside the wall surface
            spawnPos.y       = tileCenter.y;

            Quaternion spawnRot = Quaternion.LookRotation(-rayDir); // Face INTO the room

            // Apply inspector offset — rotated into locker-local space so X/Y/Z
            // always mean "sideways / up / push from wall" regardless of which wall it's on
            spawnPos += spawnRot * spawnOffset;

            GameObject locker = Instantiate(lockerPrefab, spawnPos, spawnRot, levelParent.transform);
            locker.name = $"Locker_{side}_{tileCenter}";
            spawnedLockers.Add(locker);

            // Register with NPCSpawnManager for exclusion
            npcSpawnManager?.RegisterLockerCenter(tileCenter, lockerExclusionRadius);

            return true;
        }

        return false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool IsNearProtectedArea(Vector3 pos, List<Vector3> centers)
    {
        foreach (Vector3 c in centers)
            if (Vector3.Distance(pos, c) < minDistanceFromRooms) return true;
        return false;
    }

    private bool IsTooCloseToExistingLockers(Vector3 pos)
    {
        foreach (Vector3 c in lockerCenters)
            if (Vector3.Distance(pos, c) < minLockerSpacing) return true;
        return false;
    }

    private bool IsNearCodeNumber(Vector3 pos)
    {
        foreach (Vector3 c in codeNumberPositions)
            if (Vector3.Distance(pos, c) < minDistanceFromCodeNumbers) return true;
        return false;
    }

    private static void AddCenters(List<Vector3> target, List<Vector3> source)
    {
        if (source != null) target.AddRange(source);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void ClearAll()
    {
        foreach (GameObject g in spawnedLockers)
            if (g != null) Destroy(g);

        spawnedLockers.Clear();
        lockerCenters.Clear();
    }
}
