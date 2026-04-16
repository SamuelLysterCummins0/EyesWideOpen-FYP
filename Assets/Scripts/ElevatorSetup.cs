using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reserves an elevator escape slot on the level 0 perimeter during dungeon generation,
/// then spawns the elevator prefab and a WinTrigger when the detonation sequence starts.
///
/// PLACEMENT STRATEGY — mirrors DetonationRoomSetup for a single-tile slot:
///   Finds one perimeter tile whose inward neighbour is reachable and has at
///   least one passable interior connection. Removes the perimeter tile and
///   replaces the inward tile with a fill tile so the floor is walkable right
///   up to the elevator entrance. Only the slot is reserved at generation time —
///   the prefab is NOT instantiated until SpawnElevator() is called.
///
/// Why deferred spawn?
///   The elevator should be invisible until the player presses the detonation
///   button — it gives the escape route tension and a clear objective.
///
/// Setup:
///   1. Attach this component to the same manager GameObject as DetonationManager.
///   2. Assign elevatorPrefab in the Inspector.
///      The prefab entrance should face +Z (same convention as DetonationRoomSetup).
///   3. ProceduralDungeonGenerator calls SetupLevel() during level 0 generation.
///   4. DetonationManager calls SpawnElevator() when the button is pressed.
/// </summary>
public class ElevatorSetup : MonoBehaviour
{
    public static ElevatorSetup Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Prefab")]
    [Tooltip("The elevator prefab. Entrance must face +Z (same convention as DetonationRoomSetup).")]
    public GameObject elevatorPrefab;

    [Header("Placement Settings")]
    [Tooltip("Minimum Manhattan distance (grid tiles) from the connStart / arrival tile.")]
    public int minDistFromArrival = 4;
    [Tooltip("Avoid slots within this many tiles of a dungeon corner.")]
    public int cornerAvoidance = 2;

    [Header("Rotation Tuning")]
    [Tooltip("Extra Y rotation added after the automatic edge-based orientation. " +
             "Use 180 if the elevator door faces away from the dungeon after placing.")]
    public float entranceRotationOffset = 0f;

    [Header("Position Fine-Tuning")]
    [Tooltip("World-space offset applied to the spawn position after automatic placement.")]
    public Vector3 spawnOffset = Vector3.zero;

    [Header("Win Trigger")]
    [Tooltip("Size of the BoxCollider trigger attached to the elevator at spawn time.")]
    public Vector3 winTriggerSize = new Vector3(2f, 3f, 2f);

    // ── Runtime State ──────────────────────────────────────────────────────────

    private bool      slotReserved    = false;
    private Vector3   elevatorSpawnPos;
    private Quaternion elevatorSpawnRot;
    private Transform  level0Parent;
    private GameObject spawnedElevator;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Entry Point (generation time) ─────────────────────────────────────────

    /// <summary>
    /// Called by ProceduralDungeonGenerator during level 0 generation.
    /// Finds a valid single-tile perimeter slot, removes it from the grid, places
    /// a fill tile one step inward, and stores the spawn data for SpawnElevator().
    /// </summary>
    public void SetupLevel(
        ProceduralDungeonGenerator gen,
        int                        levelIndex,
        GameObject                 levelParent,
        SpawnRoomSetup             spawnRoom,
        int                        connStartX,
        int                        connStartZ)
    {
        slotReserved   = false;
        level0Parent   = levelParent != null ? levelParent.transform : null;
        spawnedElevator = null;

        if (elevatorPrefab == null)
        {
            Debug.LogWarning("[ElevatorSetup] elevatorPrefab not assigned — elevator will not spawn.");
            return;
        }

        int   w        = gen.DungeonWidth;
        int   h        = gen.DungeonHeight;
        float tileSize = gen.TileSize;
        float levelY   = levelIndex * -gen.LevelHeight;

        HashSet<Vector2Int> reachable =
            new HashSet<Vector2Int>(gen.GetReachableTilePositions(connStartX, connStartZ));

        // Exclude the spawn room tile so the elevator can't overwrite it.
        HashSet<Vector2Int> excludeTiles = new HashSet<Vector2Int>();
        if (spawnRoom != null)
        {
            foreach (Vector3 pos in spawnRoom.GetSpawnRoomPositions())
                excludeTiles.Add(new Vector2Int(
                    Mathf.RoundToInt(pos.x / tileSize),
                    Mathf.RoundToInt(pos.z / tileSize)));
        }

        PerimeterSlot? chosen = FindValidSlot(
            gen, w, h, reachable, excludeTiles, connStartX, connStartZ);

        if (!chosen.HasValue)
        {
            Debug.LogWarning("[ElevatorSetup] Could not find a valid perimeter slot. " +
                             "Try reducing minDistFromArrival or cornerAvoidance.");
            return;
        }

        PerimeterSlot slot = chosen.Value;

        // Remove the perimeter tile and the inward tile, then fill the inward slot
        // so the player has seamless floor right up to the elevator entrance.
        gen.RemoveTile(slot.perim.x,  slot.perim.y);
        gen.RemoveTile(slot.inward.x, slot.inward.y);
        gen.PlaceFillTileAt(slot.inward.x, slot.inward.y, levelParent);

        // Compute world spawn position — identical logic to DetonationRoomSetup.
        float halfTile = tileSize * 0.5f;
        Vector3 basePos = slot.edge switch
        {
            "south" => new Vector3(slot.perim.x * tileSize,            levelY, slot.perim.y * tileSize + halfTile),
            "north" => new Vector3(slot.perim.x * tileSize,            levelY, slot.perim.y * tileSize - halfTile),
            "west"  => new Vector3(slot.perim.x * tileSize + halfTile, levelY, slot.perim.y * tileSize           ),
            "east"  => new Vector3(slot.perim.x * tileSize - halfTile, levelY, slot.perim.y * tileSize           ),
            _       => new Vector3(slot.perim.x * tileSize,            levelY, slot.perim.y * tileSize           )
        };

        elevatorSpawnPos = basePos + spawnOffset;
        elevatorSpawnRot = EdgeToRotation(slot.edge) * Quaternion.Euler(0f, entranceRotationOffset, 0f);
        slotReserved     = true;

        Debug.Log($"[ElevatorSetup] Level 0 elevator slot reserved at {elevatorSpawnPos} " +
                  $"(edge={slot.edge}, perim={slot.perim}, inward={slot.inward}).");
    }

    // ── Entry Point (detonation time) ─────────────────────────────────────────

    /// <summary>
    /// Called by DetonationManager when the detonation button is pressed.
    /// Instantiates the elevator prefab and attaches an invisible WinTrigger.
    /// </summary>
    public void SpawnElevator()
    {
        if (!slotReserved)
        {
            Debug.LogWarning("[ElevatorSetup] No slot reserved — elevator cannot spawn.");
            return;
        }

        if (spawnedElevator != null)
        {
            Debug.Log("[ElevatorSetup] Elevator already spawned.");
            return;
        }

        spawnedElevator = Object.Instantiate(
            elevatorPrefab, elevatorSpawnPos, elevatorSpawnRot, level0Parent);
        spawnedElevator.name = "Elevator_Level0_Escape";

        // Attach a WinTrigger volume at the elevator entrance.
        // WinTrigger checks DetonationManager.IsDetonationActive before firing,
        // so it only triggers victory during an active escape run.
        GameObject triggerObj = new GameObject("ElevatorWinTrigger");
        triggerObj.transform.SetParent(spawnedElevator.transform, false);
        triggerObj.transform.localPosition = Vector3.zero;

        BoxCollider col = triggerObj.AddComponent<BoxCollider>();
        col.size      = winTriggerSize;
        col.isTrigger = true;

        triggerObj.AddComponent<WinTrigger>();

        Debug.Log($"[ElevatorSetup] Elevator spawned at {elevatorSpawnPos} with WinTrigger.");
    }

    /// <summary>Destroys the spawned elevator and resets state. Called by ProceduralDungeonGenerator.ClearDungeon.</summary>
    public void ClearAll()
    {
        if (spawnedElevator != null)
        {
            Object.Destroy(spawnedElevator);
            spawnedElevator = null;
        }
        slotReserved = false;
    }

    // ── Perimeter Slot Search ──────────────────────────────────────────────────

    private struct PerimeterSlot
    {
        public Vector2Int perim;
        public Vector2Int inward;
        public string     edge;
        public int        distFromArrival;
    }

    private PerimeterSlot? FindValidSlot(
        ProceduralDungeonGenerator gen,
        int w, int h,
        HashSet<Vector2Int> reachable,
        HashSet<Vector2Int> excludeTiles,
        int connStartX, int connStartZ)
    {
        var candidates = new List<PerimeterSlot>();

        // ── South edge (z = 0) ─────────────────────────────────────────────────
        for (int x = 1; x < w - 1; x++)
        {
            if (IsNearCorner(x, 0, w, h)) continue;
            var p = new Vector2Int(x, 0);
            var i = new Vector2Int(x, 1);
            if (IsValidSlot(gen, reachable, excludeTiles, p, i, connStartX, connStartZ, "south", out int dist))
                candidates.Add(new PerimeterSlot { perim=p, inward=i, edge="south", distFromArrival=dist });
        }

        // ── North edge (z = h-1) ───────────────────────────────────────────────
        for (int x = 1; x < w - 1; x++)
        {
            if (IsNearCorner(x, h - 1, w, h)) continue;
            var p = new Vector2Int(x, h - 1);
            var i = new Vector2Int(x, h - 2);
            if (IsValidSlot(gen, reachable, excludeTiles, p, i, connStartX, connStartZ, "north", out int dist))
                candidates.Add(new PerimeterSlot { perim=p, inward=i, edge="north", distFromArrival=dist });
        }

        // ── West edge (x = 0) ──────────────────────────────────────────────────
        for (int z = 1; z < h - 1; z++)
        {
            if (IsNearCorner(0, z, w, h)) continue;
            var p = new Vector2Int(0, z);
            var i = new Vector2Int(1, z);
            if (IsValidSlot(gen, reachable, excludeTiles, p, i, connStartX, connStartZ, "west", out int dist))
                candidates.Add(new PerimeterSlot { perim=p, inward=i, edge="west", distFromArrival=dist });
        }

        // ── East edge (x = w-1) ────────────────────────────────────────────────
        for (int z = 1; z < h - 1; z++)
        {
            if (IsNearCorner(w - 1, z, w, h)) continue;
            var p = new Vector2Int(w - 1, z);
            var i = new Vector2Int(w - 2, z);
            if (IsValidSlot(gen, reachable, excludeTiles, p, i, connStartX, connStartZ, "east", out int dist))
                candidates.Add(new PerimeterSlot { perim=p, inward=i, edge="east", distFromArrival=dist });
        }

        if (candidates.Count == 0) return null;

        // Shuffle before sorting so equally-distant slots are chosen at random.
        ShuffleCandidates(candidates);
        candidates.Sort((a, b) => b.distFromArrival.CompareTo(a.distFromArrival));
        return candidates[0];
    }

    private bool IsValidSlot(
        ProceduralDungeonGenerator gen,
        HashSet<Vector2Int>        reachable,
        HashSet<Vector2Int>        excludeTiles,
        Vector2Int p, Vector2Int i,
        int connStartX, int connStartZ,
        string perimEdge,
        out int distFromArrival)
    {
        distFromArrival = 0;

        if (!IsValidInwardTile(gen, reachable, i, perimEdge)) return false;
        if (excludeTiles.Contains(i)) return false;

        if (i == new Vector2Int(connStartX, connStartZ)) return false;

        int dist = Mathf.Abs(i.x - connStartX) + Mathf.Abs(i.y - connStartZ);
        if (dist < minDistFromArrival) return false;

        distFromArrival = dist;
        return true;
    }

    // Mirrors IsValidInwardTile / HasDungeonAccess from DetonationRoomSetup exactly.
    private bool IsValidInwardTile(
        ProceduralDungeonGenerator gen,
        HashSet<Vector2Int>        reachable,
        Vector2Int                 pos,
        string                     perimEdge)
    {
        if (!reachable.Contains(pos)) return false;

        ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(pos.x, pos.y);
        if (cfg == null || !cfg.IsRoomTile()) return false;

        return HasDungeonAccess(gen, reachable, pos, perimEdge, cfg);
    }

    private bool HasDungeonAccess(
        ProceduralDungeonGenerator            gen,
        HashSet<Vector2Int>                   reachable,
        Vector2Int                            pos,
        string                                perimEdge,
        ProceduralDungeonGenerator.TileConfig cfg)
    {
        (Vector2Int delta, ProceduralDungeonGenerator.EdgeType myEdge, string name)[] dirs =
        {
            (new Vector2Int( 0,  1), cfg.north, "north"),
            (new Vector2Int( 0, -1), cfg.south, "south"),
            (new Vector2Int( 1,  0), cfg.east,  "east" ),
            (new Vector2Int(-1,  0), cfg.west,  "west" ),
        };

        foreach (var (delta, myEdge, name) in dirs)
        {
            if (name == perimEdge) continue;
            if (myEdge != ProceduralDungeonGenerator.EdgeType.Open) continue;

            Vector2Int neighbor = pos + delta;
            ProceduralDungeonGenerator.TileConfig neighborCfg = gen.GetTileConfig(neighbor.x, neighbor.y);
            if (neighborCfg == null) continue;

            if (neighborCfg.tileName == "Tiles_01_Fill") return true;

            if (!reachable.Contains(neighbor)) continue;

            ProceduralDungeonGenerator.EdgeType reciprocal = name switch
            {
                "north" => neighborCfg.south,
                "south" => neighborCfg.north,
                "east"  => neighborCfg.west,
                _       => neighborCfg.east   // "west"
            };

            if (reciprocal != ProceduralDungeonGenerator.EdgeType.Wall  &&
                reciprocal != ProceduralDungeonGenerator.EdgeType.Left  &&
                reciprocal != ProceduralDungeonGenerator.EdgeType.Right)
                return true;
        }

        return false;
    }

    private bool IsNearCorner(int x, int z, int w, int h)
    {
        bool nearX = x <= cornerAvoidance || x >= w - 1 - cornerAvoidance;
        bool nearZ = z <= cornerAvoidance || z >= h - 1 - cornerAvoidance;
        return nearX && nearZ;
    }

    private void ShuffleCandidates(List<PerimeterSlot> list)
    {
        for (int k = list.Count - 1; k > 0; k--)
        {
            int j = Random.Range(0, k + 1);
            (list[k], list[j]) = (list[j], list[k]);
        }
    }

    /// <summary>Returns the rotation that orients the prefab's +Z entrance into the dungeon.</summary>
    private static Quaternion EdgeToRotation(string edge)
    {
        return edge switch
        {
            "north" => Quaternion.Euler(0f,  180f, 0f),
            "west"  => Quaternion.Euler(0f,   90f, 0f),
            "east"  => Quaternion.Euler(0f,  -90f, 0f),
            _       => Quaternion.identity              // "south"
        };
    }
}
