using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Debug, validation, and gizmo methods split out from ProceduralDungeonGenerator
// to keep the main file focused on generation logic.
public partial class ProceduralDungeonGenerator
{
    [ContextMenu("Validate Dungeon")]
    void ValidateDungeon()
    {
        if (placedConfigs == null)
        {
            Debug.LogWarning("No dungeon to validate - generate one first");
            return;
        }

        int errorCount = 0;
        Debug.Log("=== VALIDATING DUNGEON ===");

        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedConfigs[x, z] == null) continue;
                TileConfig config = placedConfigs[x, z];
                string tileName = placedTiles[x, z] != null ? placedTiles[x, z].name : "Unknown";

                // Check perimeter with correct grid-to-tile mapping
                if (x == 0 && config.west != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - West edge (left) should be Wall, got {config.west}");
                    errorCount++;
                }
                if (x == dungeonWidth - 1 && config.east != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - East edge (right) should be Wall, got {config.east}");
                    errorCount++;
                }
                if (z == 0 && config.south != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - South edge (back) should be Wall, got {config.south}");
                    errorCount++;
                }
                if (z == dungeonHeight - 1 && config.north != EdgeType.Wall)
                {
                    Debug.LogError($"✗ ({x},{z}) {tileName}: PERIMETER ERROR - North edge (front) should be Wall, got {config.north}");
                    errorCount++;
                }

                // Check neighbor connections with correct grid mapping (using STRICT rules)
                if (x > 0 && placedConfigs[x - 1, z] != null)
                {
                    if (!EdgesMatch(config.west, placedConfigs[x - 1, z].east, strict: true))
                    {
                        // Check if it would pass with relaxed rules
                        if (EdgesMatch(config.west, placedConfigs[x - 1, z].east, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - West:{config.west} vs left neighbor East:{placedConfigs[x - 1, z].east}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - West:{config.west} vs left neighbor East:{placedConfigs[x - 1, z].east}");
                            errorCount++;
                        }
                    }
                }
                if (x < dungeonWidth - 1 && placedConfigs[x + 1, z] != null)
                {
                    if (!EdgesMatch(config.east, placedConfigs[x + 1, z].west, strict: true))
                    {
                        if (EdgesMatch(config.east, placedConfigs[x + 1, z].west, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - East:{config.east} vs right neighbor West:{placedConfigs[x + 1, z].west}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - East:{config.east} vs right neighbor West:{placedConfigs[x + 1, z].west}");
                            errorCount++;
                        }
                    }
                }
                if (z < dungeonHeight - 1 && placedConfigs[x, z + 1] != null)
                {
                    if (!EdgesMatch(config.north, placedConfigs[x, z + 1].south, strict: true))
                    {
                        if (EdgesMatch(config.north, placedConfigs[x, z + 1].south, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - North:{config.north} vs front neighbor South:{placedConfigs[x, z + 1].south}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - North:{config.north} vs front neighbor South:{placedConfigs[x, z + 1].south}");
                            errorCount++;
                        }
                    }
                }
                if (z > 0 && placedConfigs[x, z - 1] != null)
                {
                    if (!EdgesMatch(config.south, placedConfigs[x, z - 1].north, strict: true))
                    {
                        if (EdgesMatch(config.south, placedConfigs[x, z - 1].north, strict: false))
                        {
                            Debug.LogWarning($"⚠ ({x},{z}) {tileName}: RELAXED connection - South:{config.south} vs back neighbor North:{placedConfigs[x, z - 1].north}");
                        }
                        else
                        {
                            Debug.LogError($"✗ ({x},{z}) {tileName}: NEIGHBOR ERROR - South:{config.south} vs back neighbor North:{placedConfigs[x, z - 1].north}");
                            errorCount++;
                        }
                    }
                }
            }
        }

        Debug.Log("=== VALIDATION COMPLETE ===");
        if (errorCount == 0)
            Debug.Log("✓ PASSED - No illegal placements found!");
        else
            Debug.LogError($"✗ FAILED - Found {errorCount} illegal placements!");
    }

    // ── Debug connectivity summary ────────────────────────────────────────────
    void PrintLevelDebug(int levelIndex, int connStartX, int connStartZ)
    {
        if (placedTiles == null) return;

        HashSet<Vector2Int> reachable = FloodFillReachable(connStartX, connStartZ);

        int connCount = 0, isoCount = 0;
        var isoList = new List<Vector2Int>();
        for (int x = 0; x < dungeonWidth; x++)
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (placedTiles[x, z] == null) continue;
                var cfg = placedConfigs[x, z];
                if (cfg != null && cfg.tileName == "Tiles_01_Fill") continue;
                if (reachable.Contains(new Vector2Int(x, z))) connCount++;
                else { isoCount++; isoList.Add(new Vector2Int(x, z)); }
            }

        if (isoCount == 0)
        {
            Debug.Log($"[Layout] Level {levelIndex}  reachable:{connCount}  isolated:0  ✓");
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Layout] Level {levelIndex}  reachable:{connCount}  isolated:{isoCount}");
            foreach (var p in isoList)
            {
                var cfg = placedConfigs[p.x, p.y];
                sb.AppendLine($"  ! ({p.x},{p.y}) {cfg?.tileName}  N:{cfg?.north} E:{cfg?.east} S:{cfg?.south} W:{cfg?.west}");
            }
            Debug.LogWarning(sb.ToString());
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || allLevelConfigs.Count == 0) return;

        foreach (var kvp in allLevelConfigs)
        {
            int levelIndex = kvp.Key;
            TileConfig[,] configs = kvp.Value;
            float levelY = levelIndex * -levelHeight;

            for (int x = 0; x < dungeonWidth; x++)
            {
                for (int z = 0; z < dungeonHeight; z++)
                {
                    if (configs[x, z] == null) continue;
                    TileConfig config = configs[x, z];
                    Vector3 centerPos = new Vector3(x * tileSize, levelY + 0.5f, z * tileSize);

                    Gizmos.color = config.IsRoomTile() ? new Color(0.5f, 0.8f, 1f, 0.5f) : new Color(1f, 0.9f, 0.3f, 0.5f);
                    Gizmos.DrawWireCube(centerPos, Vector3.one * (tileSize * 0.9f));

                    float lineLength = tileSize * 0.35f;
                    if (config.north != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.north); Gizmos.DrawLine(centerPos, centerPos + Vector3.forward * lineLength); }
                    if (config.east != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.east); Gizmos.DrawLine(centerPos, centerPos + Vector3.right * lineLength); }
                    if (config.south != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.south); Gizmos.DrawLine(centerPos, centerPos + Vector3.back * lineLength); }
                    if (config.west != EdgeType.Wall) { Gizmos.color = GetEdgeColor(config.west); Gizmos.DrawLine(centerPos, centerPos + Vector3.left * lineLength); }
                }
            }
        }
    }

    Color GetEdgeColor(EdgeType edge)
    {
        switch (edge)
        {
            case EdgeType.Center: return Color.green;
            case EdgeType.Left: return Color.cyan;
            case EdgeType.Right: return Color.magenta;
            case EdgeType.Open: return Color.yellow;
            default: return Color.red;
        }
    }

    // Mirrors SafeRoomSetup.GetEntranceTile — one step inward from a perimeter stairs position.
    private Vector2Int DebugEntranceTile(Vector2Int sp)
    {
        if (sp.x == 0)                 return sp + new Vector2Int( 1,  0);
        if (sp.x == dungeonWidth  - 1) return sp + new Vector2Int(-1,  0);
        if (sp.y == 0)                 return sp + new Vector2Int( 0,  1);
        return                                sp + new Vector2Int( 0, -1);
    }
}
