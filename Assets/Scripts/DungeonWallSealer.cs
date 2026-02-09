using System.Collections.Generic;
using UnityEngine;

// DungeonWallSealer - spawns invisible BoxCollider barriers along every Wall edge of every tile.
// This prevents players from walking through the back face of wall geometry.
// Called by ProceduralDungeonGenerator after each level finishes generating.
public class DungeonWallSealer : MonoBehaviour
{
    [Header("Barrier Settings")]
    [Tooltip("Height of the invisible barrier - should match tile/level height")]
    public float wallHeight = 4f;
    [Tooltip("Thickness of the collider slab (invisible, just needs to block movement)")]
    public float barrierThickness = 0.25f;

    // Edge direction data — offset from tile centre to the wall face, and barrier rotation
    private static readonly Vector3[] EdgeOffsets = new Vector3[]
    {
        new Vector3( 0,  0,  1),   // North (+Z)
        new Vector3( 1,  0,  0),   // East  (+X)
        new Vector3( 0,  0, -1),   // South (-Z)
        new Vector3(-1,  0,  0),   // West  (-X)
    };

    // Barrier faces perpendicular to the edge normal: North/South are XZ-plane faces, East/West are ZX-plane faces
    // size.x = the axis parallel to the wall face, size.z = thickness
    private static readonly bool[] EdgeAxisIsZ = new bool[]
    {
        false,  // North: wall runs along X axis  → size = (tileSize, height, thickness)
        true,   // East:  wall runs along Z axis  → size = (thickness, height, tileSize)
        false,  // South: wall runs along X axis
        true,   // West:  wall runs along Z axis
    };

    public void SealLevel(ProceduralDungeonGenerator gen, int levelIndex, GameObject levelParent)
    {
        int width  = gen.DungeonWidth;
        int height = gen.DungeonHeight;
        float tileSize = gen.TileSize;
        float levelY   = levelIndex * -gen.LevelHeight;

        // Container to keep the hierarchy tidy
        GameObject sealerParent = new GameObject("WallBarriers");
        sealerParent.transform.SetParent(levelParent.transform, false);

        // Track which barriers we've already placed to avoid duplicate colliders
        // between two adjacent Wall edges (key = canonical mid-point grid pair)
        HashSet<string> placed = new HashSet<string>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                ProceduralDungeonGenerator.TileConfig cfg = gen.GetTileConfig(x, z);
                // Skip null slots and Fill tiles — Fill has (W,W,W,W) in config but no physical
                // wall meshes, so sealing its edges would incorrectly block walkable floor space.
                // Real tiles surrounding it will cover any boundaries that actually need sealing.
                if (cfg == null || cfg.tileName == "Tiles_01_Fill") continue;

                // Tile world centre
                Vector3 centre = new Vector3(x * tileSize, levelY, z * tileSize);

                PlaceBarrierIfWall(cfg.north, 0, x, z, centre, tileSize, sealerParent);
                PlaceBarrierIfWall(cfg.east,  1, x, z, centre, tileSize, sealerParent);
                PlaceBarrierIfWall(cfg.south, 2, x, z, centre, tileSize, sealerParent);
                PlaceBarrierIfWall(cfg.west,  3, x, z, centre, tileSize, sealerParent);
            }
        }
    }

    private void PlaceBarrierIfWall(
        ProceduralDungeonGenerator.EdgeType edgeType,
        int edgeIndex,
        int tileX, int tileZ,
        Vector3 tileCenter,
        float tileSize,
        GameObject parent)
    {
        if (edgeType != ProceduralDungeonGenerator.EdgeType.Wall) return;

        Vector3 offset  = EdgeOffsets[edgeIndex] * (tileSize * 0.5f);
        Vector3 pos     = tileCenter + offset + new Vector3(0, wallHeight * 0.5f, 0);

        GameObject barrier = new GameObject($"WallBarrier_{tileX}_{tileZ}_{edgeIndex}");
        barrier.transform.SetParent(parent.transform, false);
        barrier.transform.position = pos;

        BoxCollider bc = barrier.AddComponent<BoxCollider>();

        bool runsAlongZ = EdgeAxisIsZ[edgeIndex];
        if (runsAlongZ)
            bc.size = new Vector3(barrierThickness, wallHeight, tileSize);
        else
            bc.size = new Vector3(tileSize, wallHeight, barrierThickness);
    }
}
