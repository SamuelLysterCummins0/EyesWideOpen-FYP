using System.Collections.Generic;
using UnityEngine;

// Connectivity validation, flood fill, and repair methods split out from
// ProceduralDungeonGenerator to keep the main file focused on generation logic.
public partial class ProceduralDungeonGenerator
{
    // ══════════════════════════════════════════
    // CONNECTIVITY VALIDATION & REPAIR METHODS
    // ══════════════════════════════════════════

    bool IsPassableEdge(EdgeType edge)
    {
        return edge != EdgeType.Wall;
    }

    bool CanWalkBetween(int x1, int z1, int x2, int z2)
    {
        TileConfig tile1 = placedConfigs[x1, z1];
        TileConfig tile2 = placedConfigs[x2, z2];

        if (tile1 == null || tile2 == null) return false;

        bool tile1IsFill = (tile1.tileName == "Tiles_01_Fill");
        bool tile2IsFill = (tile2.tileName == "Tiles_01_Fill");

        // Both fills = both open floor, no walls anywhere, always passable
        if (tile1IsFill && tile2IsFill)
            return true;

        // Get the edges each tile has facing the other
        EdgeType edge1Facing, edge2Facing;

        if (x2 == x1 - 1) { edge1Facing = tile1.west; edge2Facing = tile2.east; }       // tile2 is West
        else if (x2 == x1 + 1) { edge1Facing = tile1.east; edge2Facing = tile2.west; }   // tile2 is East
        else if (z2 == z1 + 1) { edge1Facing = tile1.north; edge2Facing = tile2.south; }  // tile2 is North
        else if (z2 == z1 - 1) { edge1Facing = tile1.south; edge2Facing = tile2.north; }  // tile2 is South
        else return false;

        // If one tile is fill, only check the OTHER tile's edge (fill has no physical walls)
        if (tile1IsFill) return IsPassableEdge(edge2Facing);
        if (tile2IsFill) return IsPassableEdge(edge1Facing);

        return IsPassableEdge(edge1Facing) && IsPassableEdge(edge2Facing);
    }

    // ── Shared BFS flood fill ─────────────────────────────────────────────
    HashSet<Vector2Int> FloodFillReachable(int startX, int startZ)
    {
        HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
        if (!IsInBounds(startX, startZ) || placedTiles[startX, startZ] == null) return reachable;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Vector2Int start = new Vector2Int(startX, startZ);
        queue.Enqueue(start);
        reachable.Add(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y),
                new Vector2Int(current.x + 1, current.y),
                new Vector2Int(current.x, current.y + 1),
                new Vector2Int(current.x, current.y - 1)
            };
            foreach (Vector2Int n in neighbors)
            {
                if (!IsInBounds(n.x, n.y) || reachable.Contains(n)) continue;
                if (placedTiles[n.x, n.y] == null) continue;
                if (CanWalkBetween(current.x, current.y, n.x, n.y))
                {
                    reachable.Add(n);
                    queue.Enqueue(n);
                }
            }
        }
        return reachable;
    }

    // ── Public accessors for CodeNumberManager ──────────────────────────────
    public List<Vector2Int> GetReachableTilePositions(int startX, int startZ)
    {
        HashSet<Vector2Int> reachable = FloodFillReachable(startX, startZ);
        List<Vector2Int> result = new List<Vector2Int>();
        foreach (Vector2Int pos in reachable)
        {
            TileConfig cfg = placedConfigs[pos.x, pos.y];
            if (cfg != null && cfg.tileName != "Tiles_01_Fill")
                result.Add(pos);
        }
        return result;
    }

    public TileConfig GetTileConfig(int x, int z) => placedConfigs?[x, z];
    public GameObject GetPlacedTile(int x, int z) => placedTiles?[x, z];
    public float TileSize => tileSize;
    public float LevelHeight => levelHeight;
    public int DungeonWidth => dungeonWidth;
    public int DungeonHeight => dungeonHeight;
    public List<Vector2Int> StairsPositions => stairsPositions;

    List<Vector2Int> FindIsolatedTiles(int startX, int startZ)
    {
        HashSet<Vector2Int> reachable = FloodFillReachable(startX, startZ);
        List<Vector2Int> isolated = new List<Vector2Int>();
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
                if (placedTiles[x, z] != null && !reachable.Contains(new Vector2Int(x, z)))
                    isolated.Add(new Vector2Int(x, z));
        return isolated;
    }

    bool TryRepairIsolatedTile(int targetX, int targetZ, int startX, int startZ, GameObject parent)
    {
        TileConfig targetConfig = placedConfigs[targetX, targetZ];
        if (targetConfig == null) return false;

        // Strategy 1: If the isolated tile is a fill tile, try replacing it with a real tile
        if (targetConfig.tileName == "Tiles_01_Fill")
        {
            SafeDestroy(placedTiles[targetX, targetZ]);
            placedTiles[targetX, targetZ] = null;
            placedConfigs[targetX, targetZ] = null;

            bool placed = TryPlaceCompatibleTile(targetX, targetZ, parent);
            if (placed)
                return true;
        }

        // Strategy 2: Find which neighbors block passage and try replacing them
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        queue.Enqueue(new Vector2Int(targetX, targetZ));
        visited.Add(new Vector2Int(targetX, targetZ));

        List<Vector2Int> blockingCandidates = new List<Vector2Int>();

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            Vector2Int[] neighbors = {
                new Vector2Int(current.x - 1, current.y),
                new Vector2Int(current.x + 1, current.y),
                new Vector2Int(current.x, current.y + 1),
                new Vector2Int(current.x, current.y - 1)
            };

            foreach (Vector2Int n in neighbors)
            {
                if (!IsInBounds(n.x, n.y)) continue;
                if (visited.Contains(n)) continue;
                if (placedTiles[n.x, n.y] == null) continue;

                visited.Add(n);

                if (!CanWalkBetween(current.x, current.y, n.x, n.y))
                    blockingCandidates.Add(n);
                else
                    queue.Enqueue(n);
            }
        }

        // Try replacing each blocking candidate until one works
        foreach (Vector2Int blocker in blockingCandidates)
        {
            int bx = blocker.x;
            int bz = blocker.y;

            GameObject oldTileObj = placedTiles[bx, bz];
            TileConfig oldConfig = placedConfigs[bx, bz];
            string oldPrefabName = oldConfig != null ? oldConfig.tileName : null;

            SafeDestroy(oldTileObj);
            placedTiles[bx, bz] = null;
            placedConfigs[bx, bz] = null;

            if (TryPlaceCompatibleTile(bx, bz, parent))
            {
                List<Vector2Int> stillIsolated = FindIsolatedTiles(startX, startZ);
                bool targetStillIsolated = stillIsolated.Contains(new Vector2Int(targetX, targetZ));
                if (!targetStillIsolated)
                {
#if UNITY_EDITOR
                    Debug.Log($"Repair: replaced ({bx},{bz}) to connect ({targetX},{targetZ})");
#endif
                    return true;
                }
                SafeDestroy(placedTiles[bx, bz]);
            }

            // Restore old tile
            placedTiles[bx, bz] = null;
            placedConfigs[bx, bz] = null;
            if (oldPrefabName != null)
            {
                GameObject prefab = System.Array.Find(allTilePrefabs, p => p != null && p.name == oldPrefabName);
                if (prefab != null)
                    PlaceTile(bx, bz, prefab, parent);
            }
        }

        return false;
    }

    bool ValidateFullConnectivity(int startX, int startZ, GameObject parent, int levelIndex)
    {
        int errorCount = 0;

        // 1. Check all tiles are reachable
        List<Vector2Int> isolated = FindIsolatedTiles(startX, startZ);
        if (isolated.Count > 0)
        {
            Debug.LogError($"✗ Level {levelIndex} validation FAILED: {isolated.Count} isolated tiles");
            foreach (Vector2Int pos in isolated)
            {
                Debug.Log($"    - Isolated tile at ({pos.x},{pos.y})");
            }
            errorCount += isolated.Count;
        }

        // 2. Check perimeter for walk-off points
        int walkoffCount = 0;
        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedTiles[x, z] == null) continue;
                TileConfig config = placedConfigs[x, z];

                if (config.tileName == "Tiles_01_Fill")
                {
                    bool isPerimeter = (x == 0 || x == dungeonWidth - 1 ||
                                        z == 0 || z == dungeonHeight - 1);
                    if (isPerimeter)
                    {
                        Debug.LogError($"✗ Fill tile at perimeter ({x},{z}) - walk-off hazard! (config says Wall but no physical walls)");
                        walkoffCount++;
                    }
                }

                if (x == 0 && config.west != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): West edge is {config.west}");
                    walkoffCount++;
                }
                if (x == dungeonWidth - 1 && config.east != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): East edge is {config.east}");
                    walkoffCount++;
                }
                if (z == 0 && config.south != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): South edge is {config.south}");
                    walkoffCount++;
                }
                if (z == dungeonHeight - 1 && config.north != EdgeType.Wall)
                {
                    Debug.LogError($"✗ Walk-off at ({x},{z}): North edge is {config.north}");
                    walkoffCount++;
                }
            }
        }
        errorCount += walkoffCount;

        // 3. Check minimum tile count reached (at least 80% of target)
        int tileCount = 0;
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
                if (placedTiles[x, z] != null) tileCount++;

        if (tileCount < targetTileCount * 0.8f)
        {
            Debug.LogError($"✗ Level {levelIndex} validation FAILED: Only {tileCount}/{targetTileCount} tiles (need at least 80%)");
            errorCount++;
        }

        if (errorCount == 0)
        {
            Debug.Log($"✓ Level {levelIndex} validation PASSED: {tileCount} tiles, all connected, no walk-offs");
            return true;
        }
        else
        {
            Debug.LogError($"✗ Level {levelIndex} validation FAILED: {errorCount} total errors");
            return false;
        }
    }
}
