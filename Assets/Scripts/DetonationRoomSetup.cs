using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places the Detonation Room on the last dungeon level (the level with no departure stairs).
///
/// PLACEMENT STRATEGY — mirrors how every other special room works:
///   Finds TWO adjacent perimeter tile slots on the same edge, then places the room
///   prefab extending OUTWARD from the dungeon boundary. Only the 2 outer perimeter
///   tiles are removed from the grid. The 2 inward tiles (one step inside the dungeon)
///   remain intact and act as the player's approach area / keypad interaction zone.
///
///   Why perimeter pairs?
///   • Perimeter tiles are NEVER passageways — removing them cannot disconnect the dungeon.
///   • This is identical to how the stairway, safe room, spawn room, and computer room
///     all avoid blocking dungeon connectivity.
///   • Adjacent pairs are common, so the room almost always finds a valid spot.
///
/// DetonationRoom Prefab requirements:
///   • Entrance (door + keypad) on the +Z (forward) face of the prefab root.
///   • A button mesh at local position ~(0, 1, -tileSize) with GazeButtonInteraction.
///   • SlidingDoor + Keypad + GazeKeypadInteraction + CameraPosition child on the entrance face.
///   • The room body extends in the -Z direction (outward away from the dungeon).
///   The code rotates the prefab so its +Z entrance always faces into the dungeon,
///   regardless of which edge it ends up on.
/// </summary>
public class DetonationRoomSetup : MonoBehaviour
{
    public static DetonationRoomSetup Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Prefab")]
    [Tooltip("The DetonationRoom prefab. Must have GazeKeypadInteraction, SlidingDoor, and GazeButtonInteraction.")]
    public GameObject detonationRoomPrefab;

    [Header("Placement Settings")]
    [Tooltip("Minimum Manhattan distance (grid tiles) from the connStart / arrival tile.")]
    public int minDistFromArrival = 4;
    [Tooltip("Avoid pairing slots within this many tiles of any dungeon corner.")]
    public int cornerAvoidance = 2;

    [Header("Rotation Tuning")]
    [Tooltip("Extra Y rotation added after the automatic edge-based orientation. " +
             "Use 180 if the door faces away from the dungeon after placing.")]
    public float entranceRotationOffset = 0f;

    [Header("Position Fine-Tuning")]
    [Tooltip("World-space offset applied to the room spawn position AFTER the automatic " +
             "edge-based placement. Use this to nudge the room if the entrance doesn't " +
             "line up perfectly with your prefab's pivot. (0,0,0 = no adjustment.)")]
    public Vector3 roomSpawnOffset = Vector3.zero;

    // ── Runtime State ──────────────────────────────────────────────────────────

    private readonly Dictionary<int, Vector3> entrancePosByLevel  = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Vector3> roomCenterByLevel   = new Dictionary<int, Vector3>();
    private readonly List<GameObject>         spawnedRooms        = new List<GameObject>();

    public Vector3 GetEntrancePosition(int levelIndex)
        => entrancePosByLevel.TryGetValue(levelIndex, out var v) ? v : Vector3.zero;

    public List<Vector3> GetRoomCenters() => new List<Vector3>(roomCenterByLevel.Values);

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Entry Point ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ProceduralDungeonGenerator.GenerateLevel on the last level.
    /// Finds a valid perimeter pair, removes those 2 tiles, and spawns the room prefab.
    /// </summary>
    public void SetupLevel(
        ProceduralDungeonGenerator gen,
        int                        levelIndex,
        GameObject                 levelParent,
        SpawnRoomSetup             spawnRoom,
        ComputerRoomSetup          computerRoom,
        HiddenRoomSetup            hiddenRoom,
        int                        connStartX,
        int                        connStartZ)
    {
        if (detonationRoomPrefab == null)
        {
            Debug.LogError("[DetonationRoomSetup] detonationRoomPrefab not assigned in Inspector!");
            return;
        }

        int   w        = gen.DungeonWidth;
        int   h        = gen.DungeonHeight;
        float tileSize = gen.TileSize;
        float levelY   = levelIndex * -gen.LevelHeight;

        // Build reachable set to validate inward tiles
        HashSet<Vector2Int> reachable =
            new HashSet<Vector2Int>(gen.GetReachableTilePositions(connStartX, connStartZ));

        // ── Find a valid perimeter pair ────────────────────────────────────────

        PerimeterPair? chosen = FindValidPair(gen, w, h, reachable, connStartX, connStartZ);

        if (!chosen.HasValue)
        {
            Debug.LogWarning("[DetonationRoomSetup] Could not find a valid perimeter pair. " +
                             "Try reducing minDistFromArrival or cornerAvoidance.");
            return;
        }

        PerimeterPair pair = chosen.Value;

        // ── Remove the 2 perimeter tiles AND the 2 inward tiles ───────────────
        // Removing the inward tiles clears any wall geometry that would otherwise
        // sit directly in front of the door opening.
        gen.RemoveTile(pair.perim1.x,  pair.perim1.y);
        gen.RemoveTile(pair.perim2.x,  pair.perim2.y);
        gen.RemoveTile(pair.inward1.x, pair.inward1.y);
        gen.RemoveTile(pair.inward2.x, pair.inward2.y);

        // Replace the inward slots with fill tiles so the player has solid,
        // wall-free floor right up to the door. Fill tiles are always passable
        // so the reachability flood-fill runs straight through them.
        gen.PlaceFillTileAt(pair.inward1.x, pair.inward1.y, levelParent);
        gen.PlaceFillTileAt(pair.inward2.x, pair.inward2.y, levelParent);

        // ── Compute world positions ────────────────────────────────────────────

        float halfTile = tileSize * 0.5f;
        float midX     = (pair.perim1.x + pair.perim2.x) * 0.5f * tileSize;
        float midZ     = (pair.perim1.y + pair.perim2.y) * 0.5f * tileSize;

        Vector3 spawnPos = pair.edge switch
        {
            "south" => new Vector3(midX,            levelY, midZ + halfTile),
            "north" => new Vector3(midX,            levelY, midZ - halfTile),
            "west"  => new Vector3(midX + halfTile, levelY, midZ           ),
            "east"  => new Vector3(midX - halfTile, levelY, midZ           ),
            _       => new Vector3(midX,            levelY, midZ           )
        };

        spawnPos += roomSpawnOffset;

        // Entrance (respawn) point: one tile deeper than the (now removed) inward tiles,
        // so the player respawns on solid dungeon floor inside the dungeon.
        Vector2Int deeper1 = pair.inward1 + pair.edge switch
        {
            "south" => new Vector2Int(0,  1),
            "north" => new Vector2Int(0, -1),
            "west"  => new Vector2Int(1,  0),
            _       => new Vector2Int(-1, 0)   // "east"
        };
        Vector2Int deeper2 = pair.inward2 + pair.edge switch
        {
            "south" => new Vector2Int(0,  1),
            "north" => new Vector2Int(0, -1),
            "west"  => new Vector2Int(1,  0),
            _       => new Vector2Int(-1, 0)   // "east"
        };
        Vector3 entrancePos = new Vector3(
            ((deeper1.x + deeper2.x) * 0.5f) * tileSize,
            levelY + 1f,
            ((deeper1.y + deeper2.y) * 0.5f) * tileSize);

        roomCenterByLevel[levelIndex]   = spawnPos;
        entrancePosByLevel[levelIndex]  = entrancePos;

        // ── Rotate prefab so its +Z entrance faces into the dungeon ────────────

        Quaternion baseRotation  = EdgeToRotation(pair.edge);
        Quaternion finalRotation = baseRotation * Quaternion.Euler(0f, entranceRotationOffset, 0f);

        // ── Spawn the prefab ───────────────────────────────────────────────────

        GameObject room = Object.Instantiate(
            detonationRoomPrefab, spawnPos, finalRotation, levelParent.transform);
        room.name = $"DetonationRoom_Level{levelIndex}";
        spawnedRooms.Add(room);

        Debug.Log($"[DetonationRoomSetup] Level {levelIndex}: room placed at {spawnPos} " +
                  $"(edge={pair.edge}, perim=({pair.perim1}+{pair.perim2}), " +
                  $"inward=({pair.inward1}+{pair.inward2})).");
    }

    /// <summary>Destroys all spawned rooms. Called by ProceduralDungeonGenerator.ClearDungeon.</summary>
    public void ClearAll()
    {
        foreach (var room in spawnedRooms)
            if (room != null) Object.Destroy(room);

        spawnedRooms.Clear();
        roomCenterByLevel.Clear();
        entrancePosByLevel.Clear();
    }

    // ── Perimeter Pair Search ──────────────────────────────────────────────────

    private struct PerimeterPair
    {
        public Vector2Int perim1, perim2;   // the 2 perimeter tiles to remove
        public Vector2Int inward1, inward2; // the 2 inward tiles (stay in dungeon)
        public string     edge;             // "south" | "north" | "west" | "east"
        public int        distFromArrival;  // for sorting — prefer far from connStart
    }

    private PerimeterPair? FindValidPair(
        ProceduralDungeonGenerator gen,
        int w, int h,
        HashSet<Vector2Int> reachable,
        int connStartX, int connStartZ)
    {
        var candidates = new List<PerimeterPair>();

        // ── South edge (z = 0) ─────────────────────────────────────────────────
        for (int x = 1; x < w - 2; x++)
        {
            if (IsNearCorner(x, 0, w, h)) continue;
            var p1 = new Vector2Int(x,     0);
            var p2 = new Vector2Int(x + 1, 0);
            var i1 = new Vector2Int(x,     1);
            var i2 = new Vector2Int(x + 1, 1);
            if (IsValidPair(gen, reachable, p1, p2, i1, i2, connStartX, connStartZ,
                            "south", out int dist))
                candidates.Add(new PerimeterPair
                    { perim1=p1, perim2=p2, inward1=i1, inward2=i2,
                      edge="south", distFromArrival=dist });
        }

        // ── North edge (z = h-1) ───────────────────────────────────────────────
        for (int x = 1; x < w - 2; x++)
        {
            if (IsNearCorner(x, h - 1, w, h)) continue;
            var p1 = new Vector2Int(x,     h - 1);
            var p2 = new Vector2Int(x + 1, h - 1);
            var i1 = new Vector2Int(x,     h - 2);
            var i2 = new Vector2Int(x + 1, h - 2);
            if (IsValidPair(gen, reachable, p1, p2, i1, i2, connStartX, connStartZ,
                            "north", out int dist))
                candidates.Add(new PerimeterPair
                    { perim1=p1, perim2=p2, inward1=i1, inward2=i2,
                      edge="north", distFromArrival=dist });
        }

        // ── West edge (x = 0) ──────────────────────────────────────────────────
        for (int z = 1; z < h - 2; z++)
        {
            if (IsNearCorner(0, z, w, h)) continue;
            var p1 = new Vector2Int(0, z);
            var p2 = new Vector2Int(0, z + 1);
            var i1 = new Vector2Int(1, z);
            var i2 = new Vector2Int(1, z + 1);
            if (IsValidPair(gen, reachable, p1, p2, i1, i2, connStartX, connStartZ,
                            "west", out int dist))
                candidates.Add(new PerimeterPair
                    { perim1=p1, perim2=p2, inward1=i1, inward2=i2,
                      edge="west", distFromArrival=dist });
        }

        // ── East edge (x = w-1) ────────────────────────────────────────────────
        for (int z = 1; z < h - 2; z++)
        {
            if (IsNearCorner(w - 1, z, w, h)) continue;
            var p1 = new Vector2Int(w - 1, z);
            var p2 = new Vector2Int(w - 1, z + 1);
            var i1 = new Vector2Int(w - 2, z);
            var i2 = new Vector2Int(w - 2, z + 1);
            if (IsValidPair(gen, reachable, p1, p2, i1, i2, connStartX, connStartZ,
                            "east", out int dist))
                candidates.Add(new PerimeterPair
                    { perim1=p1, perim2=p2, inward1=i1, inward2=i2,
                      edge="east", distFromArrival=dist });
        }

        if (candidates.Count == 0) return null;

        // Pick the candidate farthest from the player's arrival point (connStart).
        candidates.Sort((a, b) => b.distFromArrival.CompareTo(a.distFromArrival));
        return candidates[0];
    }

    /// <summary>
    /// Returns true when both inward tiles are valid approach tiles for the door:
    ///   1. In the dungeon's reachable set.
    ///   2. Are room tiles (all edges Open or Wall).
    ///   3. Neither is the connStart tile.
    ///   4. Far enough from the arrival tile.
    ///   5. Each has at least one passable dungeon-interior connection (non-perimeter side)
    ///      so the tiles remain accessible after the perimeter tiles are removed.
    /// </summary>
    private bool IsValidPair(
        ProceduralDungeonGenerator gen,
        HashSet<Vector2Int>        reachable,
        Vector2Int p1, Vector2Int p2,
        Vector2Int i1, Vector2Int i2,
        int connStartX, int connStartZ,
        string perimEdge,
        out int distFromArrival)
    {
        distFromArrival = 0;

        if (!IsValidInwardTile(gen, reachable, i1, perimEdge)) return false;
        if (!IsValidInwardTile(gen, reachable, i2, perimEdge)) return false;

        var connStart = new Vector2Int(connStartX, connStartZ);
        if (i1 == connStart || i2 == connStart) return false;

        int d1 = Mathf.Abs(i1.x - connStartX) + Mathf.Abs(i1.y - connStartZ);
        int d2 = Mathf.Abs(i2.x - connStartX) + Mathf.Abs(i2.y - connStartZ);
        if (Mathf.Min(d1, d2) < minDistFromArrival) return false;

        distFromArrival = (d1 + d2) / 2;
        return true;
    }

    /// <summary>
    /// An inward tile is valid when it is reachable, is a room tile, and has at least
    /// one open connection into the dungeon interior on a non-perimeter side.
    /// The dungeon-interior check (HasDungeonAccess) ensures the tile stays reachable
    /// after its adjacent perimeter tile is removed.
    /// </summary>
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

    /// <summary>
    /// Returns true if the tile has at least one open, reciprocally-passable edge
    /// on a non-perimeter side whose neighbour is in the reachable set.
    /// Fill tile neighbours are always accepted (passable but excluded from the
    /// reachable set by GetReachableTilePositions).
    /// Mirrors the hasAtLeastOneValidDoor check in ComputerRoomSetup.
    /// </summary>
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

            // Fill tiles are always passable — accept without a reachable-set check
            // because GetReachableTilePositions deliberately excludes Fill tiles.
            if (neighborCfg.tileName == "Tiles_01_Fill") return true;

            if (!reachable.Contains(neighbor)) continue;

            ProceduralDungeonGenerator.EdgeType reciprocal = name switch
            {
                "north" => neighborCfg.south,
                "south" => neighborCfg.north,
                "east"  => neighborCfg.west,
                _       => neighborCfg.east  // "west"
            };

            if (reciprocal != ProceduralDungeonGenerator.EdgeType.Wall &&
                reciprocal != ProceduralDungeonGenerator.EdgeType.Left  &&
                reciprocal != ProceduralDungeonGenerator.EdgeType.Right)
                return true;
        }

        return false;
    }

    private bool IsNearCorner(int x, int z, int w, int h)
    {
        bool nearX = (x <= cornerAvoidance || x >= w - 1 - cornerAvoidance);
        bool nearZ = (z <= cornerAvoidance || z >= h - 1 - cornerAvoidance);
        return nearX && nearZ;
    }

    // ── Rotation Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the rotation that points the prefab's +Z (entrance face) into the dungeon.
    ///
    ///   South edge → entrance must face world +Z (north, into dungeon) → no rotation
    ///   North edge → entrance must face world -Z (south, into dungeon) → 180°
    ///   West  edge → entrance must face world +X (east,  into dungeon) →  90°
    ///   East  edge → entrance must face world -X (west,  into dungeon) → -90°
    /// </summary>
    private static Quaternion EdgeToRotation(string edge)
    {
        return edge switch
        {
            "north" => Quaternion.Euler(0f,  180f, 0f),
            "west"  => Quaternion.Euler(0f,   90f, 0f),
            "east"  => Quaternion.Euler(0f,  -90f, 0f),
            _       => Quaternion.identity              // "south" — no rotation needed
        };
    }
}
