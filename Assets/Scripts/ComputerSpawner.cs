using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one ComputerTerminal prefab per level onto a valid, reachable floor tile
/// that has at least one interior wall to sit against.
/// Call pattern is identical to CodeNumberManager.InitializeForLevel() so the dungeon
/// generator only needs one extra line to drive it.
/// </summary>
public class ComputerSpawner : MonoBehaviour
{
    public static ComputerSpawner Instance { get; private set; }

    [Header("Prefab")]
    [Tooltip("The ComputerTerminal prefab — must have ComputerInteraction.cs and a BoxCollider on the root.")]
    [SerializeField] private GameObject computerPrefab;

    [Header("Placement")]
    [Tooltip("How far from the wall surface to offset the computer (so it doesn't clip into the wall).")]
    [SerializeField] private float wallClearance = 0.4f;

    [Tooltip("Minimum world-space distance from the player start tile before a computer can spawn. Prevents it appearing right at spawn.")]
    [SerializeField] private float minDistanceFromStart = 16f;

    [Tooltip("How far to raycast from a tile centre toward a wall to confirm geometry is there.")]
    [SerializeField] private float wallRaycastDistance = 2.5f;

    // Per-level tracking so ClearAll() can destroy the right objects
    private readonly Dictionary<int, GameObject> spawnedComputers = new Dictionary<int, GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Entry point — mirrors CodeNumberManager.InitializeForLevel ───────────

    /// <summary>
    /// Called by ProceduralDungeonGenerator after a level finishes building.
    /// Destroys any previous computer for this level and spawns a fresh one.
    /// </summary>
    public void InitializeForLevel(ProceduralDungeonGenerator generator, int levelIndex, int startX, int startZ)
    {
        // Remove any computer that was placed on this level previously
        ClearLevel(levelIndex);

        if (computerPrefab == null)
        {
            Debug.LogError("[ComputerSpawner] computerPrefab is not assigned!");
            return;
        }

        // All tiles reachable from the connectivity anchor via BFS — same call as CodeNumberManager
        List<Vector2Int> reachable = generator.GetReachableTilePositions(startX, startZ);

        if (reachable.Count == 0)
        {
            Debug.LogWarning("[ComputerSpawner] No reachable tiles found.");
            return;
        }

        // Player start world position used for minimum-distance check
        float tileSize = generator.TileSize;
        Vector3 startWorld = new Vector3(startX * tileSize, 0f, startZ * tileSize);

        // Shuffle so we pick a random valid tile, not always the same area of the dungeon
        ShuffleList(reachable);

        foreach (Vector2Int gridPos in reachable)
        {
            GameObject tile = generator.GetPlacedTile(gridPos.x, gridPos.y);
            ProceduralDungeonGenerator.TileConfig config = generator.GetTileConfig(gridPos.x, gridPos.y);

            if (tile == null || config == null) continue;

            // Must be far enough from the player start so it isn't trivially easy to find
            Vector3 tileWorld = new Vector3(gridPos.x * tileSize, 0f, gridPos.y * tileSize);
            if (Vector3.Distance(tileWorld, startWorld) < minDistanceFromStart) continue;

            // Find a wall face on this tile to place the computer against
            Vector3? spawnPos = FindFloorPositionAgainstWall(tile, config, tileSize, out Vector3 wallNormal);
            if (spawnPos == null) continue;

            // All checks passed — spawn and store, then stop
            SpawnComputer(spawnPos.Value, wallNormal, levelIndex);
            Debug.Log($"[ComputerSpawner] Level {levelIndex}: computer spawned at {spawnPos.Value} (grid {gridPos}).");
            return;
        }

        Debug.LogWarning("[ComputerSpawner] Could not find a valid tile for the computer. Try reducing minDistanceFromStart.");
    }

    // ── Placement ─────────────────────────────────────────────────────────────

    private void SpawnComputer(Vector3 position, Vector3 wallNormal, int levelIndex)
    {
        // Face the computer INTO the room (screen toward the player), same rotation as CodeNumber
        Quaternion rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);

        GameObject obj = Instantiate(computerPrefab, position, rotation);
        obj.name = $"ComputerTerminal_L{levelIndex}";

        // Tell the ComputerInteraction which level this terminal belongs to so the
        // MazeMinigame reports the correct digit slot to CodeNumberManager.
        ComputerInteraction ci = obj.GetComponentInChildren<ComputerInteraction>(true);
        if (ci == null) ci = obj.GetComponent<ComputerInteraction>();
        if (ci != null)
            ci.levelIndex = levelIndex;
        else
            Debug.LogError($"[ComputerSpawner] ComputerInteraction not found on '{obj.name}' — digit slot will default to level 0!");

        spawnedComputers[levelIndex] = obj;
    }

    /// <summary>
    /// Scans the four faces of a tile for an EdgeType.Wall edge, then places the
    /// computer on the floor against that wall — identical raycast logic to
    /// CodeNumberManager.FindValidWallSurface but at floor height instead of eye height.
    /// </summary>
    private Vector3? FindFloorPositionAgainstWall(GameObject tile, ProceduralDungeonGenerator.TileConfig config,
                                                   float tileSize, out Vector3 outNormal)
    {
        outNormal = Vector3.forward;
        float halfSize = tileSize * 0.5f;

        // (offset to wall face centre, inward normal pointing into room, edge type)
        (Vector3 offset, Vector3 inwardNormal, ProceduralDungeonGenerator.EdgeType edge)[] faces =
        {
            (Vector3.forward * halfSize, Vector3.back,    config.north),
            (Vector3.back    * halfSize, Vector3.forward, config.south),
            (Vector3.right   * halfSize, Vector3.left,    config.east),
            (Vector3.left    * halfSize, Vector3.right,   config.west),
        };

        // Randomise which wall face we try first
        for (int i = faces.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = faces[i]; faces[i] = faces[j]; faces[j] = tmp;
        }

        foreach (var (offset, inwardNormal, edge) in faces)
        {
            if (edge != ProceduralDungeonGenerator.EdgeType.Wall) continue;

            // Raycast from tile centre toward this wall face to confirm geometry is there —
            // identical to CodeNumberManager, just at floor height (0.1f above floor)
            Vector3 rayOrigin = tile.transform.position + Vector3.up * 0.1f;
            Vector3 rayDir    = offset.normalized;

            Vector3 spawnPos;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, wallRaycastDistance))
            {
                // Pull the spawn point off the wall surface by wallClearance so the mesh doesn't clip
                spawnPos   = hit.point + inwardNormal * wallClearance;
                spawnPos.y = tile.transform.position.y; // floor level
            }
            else
            {
                // Fallback: trust the config edge and calculate the position directly
                spawnPos   = tile.transform.position + offset + inwardNormal * wallClearance;
                spawnPos.y = tile.transform.position.y;
            }

            outNormal = inwardNormal;
            return spawnPos;
        }

        return null; // No wall face on this tile
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void ClearLevel(int levelIndex)
    {
        if (!spawnedComputers.TryGetValue(levelIndex, out GameObject obj)) return;
        if (obj != null)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
        spawnedComputers.Remove(levelIndex);
    }

    /// <summary>
    /// Called by ProceduralDungeonGenerator.ClearDungeon() — destroys all computers.
    /// </summary>
    public void ClearAll()
    {
        foreach (var kvp in spawnedComputers)
        {
            if (kvp.Value != null)
            {
                if (Application.isPlaying) Destroy(kvp.Value);
                else DestroyImmediate(kvp.Value);
            }
        }
        spawnedComputers.Clear();
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j    = Random.Range(0, i + 1);
            T   tmp  = list[i];
            list[i]  = list[j];
            list[j]  = tmp;
        }
    }
}
