using System.Collections.Generic;

// All tile edge configurations extracted from ProceduralDungeonGenerator.
// Keeps the generator file focused on logic, not data.
public static class TileConfigData
{
    public static void Populate(Dictionary<string, ProceduralDungeonGenerator.TileConfig> configs)
    {
        var W = ProceduralDungeonGenerator.EdgeType.Wall;
        var C = ProceduralDungeonGenerator.EdgeType.Center;
        var L = ProceduralDungeonGenerator.EdgeType.Left;
        var R = ProceduralDungeonGenerator.EdgeType.Right;
        var O = ProceduralDungeonGenerator.EdgeType.Open;

        // ══════════════════════════════════════════
        // CORRIDOR TILES (__ double underscore = + in docs)
        // Parameter order: North, East, South, West
        // ══════════════════════════════════════════

        // __Corner_01 series - center openings
        Add(configs, "Tiles__Corner_01_A", C, W, W, C);  // F + L
        Add(configs, "Tiles__Corner_01_B", C, C, W, W);  // F + R
        Add(configs, "Tiles__Corner_01_C", W, W, C, C);  // L + B
        Add(configs, "Tiles__Corner_01_D", W, C, C, W);  // R + B

        // __Corner_02 series - mixed center and open
        Add(configs, "Tiles__Corner_02_A", C, O, O, C);  // F + L and R + B NO WALLS
        Add(configs, "Tiles__Corner_02_B", C, C, O, O);  // F + R and L + B NO WALLS
        Add(configs, "Tiles__Corner_02_C", O, O, C, C);  // L + B and R + F NO WALLS
        Add(configs, "Tiles__Corner_02_D", O, C, C, O);  // R + B and L + F NO WALLS

        // __Cross
        Add(configs, "Tiles__Cross", C, C, C, C);  // F + B + L + R

        // __Halls
        Add(configs, "Tiles__Halls_01_A", C, W, C, W);  // F + B
        Add(configs, "Tiles__Halls_01_B", W, C, W, C);  // L + R

        // __RoomEnd
        Add(configs, "Tiles__RoomEnd_01_A", C, W, W, W);  // F
        Add(configs, "Tiles__RoomEnd_01_B", W, W, W, C);  // L
        Add(configs, "Tiles__RoomEnd_01_C", W, C, W, W);  // R
        Add(configs, "Tiles__RoomEnd_01_D", W, W, C, W);  // B

        // __Room_Hole (same as RoomEnd)
        Add(configs, "Tiles__Room_Hole_01_A", C, W, W, W);
        Add(configs, "Tiles__Room_Hole_01_B", W, W, W, C);
        Add(configs, "Tiles__Room_Hole_01_C", W, C, W, W);
        Add(configs, "Tiles__Room_Hole_01_D", W, W, C, W);

        // __Room_HoleCeiling
        Add(configs, "Tiles__Room_HoleCeiling_01_A", C, W, W, W);
        Add(configs, "Tiles__Room_HoleCeiling_01_B", W, W, W, C);
        Add(configs, "Tiles__Room_HoleCeiling_01_C", W, C, W, W);
        Add(configs, "Tiles__Room_HoleCeiling_01_D", W, W, C, W);

        // __Room_HoleFloorCeiling
        Add(configs, "Tiles__Room_HoleFloorCeiling_01_A", C, W, W, W);
        Add(configs, "Tiles__Room_HoleFloorCeiling_01_B", W, W, W, C);
        Add(configs, "Tiles__Room_HoleFloorCeiling_01_C", W, C, W, W);
        Add(configs, "Tiles__Room_HoleFloorCeiling_01_D", W, W, C, W);

        // __Room_HoleFloor
        Add(configs, "Tiles__Room_HoleFloor_01_A", C, W, W, W);
        Add(configs, "Tiles__Room_HoleFloor_01_B", W, W, W, C);
        Add(configs, "Tiles__Room_HoleFloor_01_C", W, C, W, W);
        Add(configs, "Tiles__Room_HoleFloor_01_D", W, W, C, W);

        // __RoomStairs
        Add(configs, "Tiles__RoomStairs_01_A", C, W, W, W);
        Add(configs, "Tiles__RoomStairs_01_B", W, W, W, C);
        Add(configs, "Tiles__RoomStairs_01_C", W, C, W, W);
        Add(configs, "Tiles__RoomStairs_01_D", W, W, C, W);

        // __Side_01 - T-junctions
        Add(configs, "Tiles__Side_01_A", C, C, W, C);  // F + L + R
        Add(configs, "Tiles__Side_01_B", C, W, C, C);  // F + L + B
        Add(configs, "Tiles__Side_01_C", C, C, C, W);  // F + R + B
        Add(configs, "Tiles__Side_01_D", W, C, C, C);  // L + R + B

        // __Side_02 - Mixed
        Add(configs, "Tiles__Side_02_A", C, C, O, C);  // F + L + R and B NO WALLS
        Add(configs, "Tiles__Side_02_B", C, O, C, C);  // F + L + B and R NO WALLS
        Add(configs, "Tiles__Side_02_C", C, C, C, O);  // F + R + B and L NO WALLS
        Add(configs, "Tiles__Side_02_D", O, C, C, C);  // L + R + B and F NO WALLS

        // ══════════════════════════════════════════
        // ROOM TILES (BasicCorner/BasicSide)
        // ══════════════════════════════════════════

        // BasicCorner_01 - 2 adjacent open edges
        Add(configs, "Tiles_BasicCorner_01_A", O, W, W, O);  // F + L NO WALLS
        Add(configs, "Tiles_BasicCorner_01_B", O, O, W, W);  // F + R NO WALLS
        Add(configs, "Tiles_BasicCorner_01_C", W, W, O, O);  // L + B NO WALLS
        Add(configs, "Tiles_BasicCorner_01_D", W, O, O, W);  // R + B NO WALLS

        // BasicCorner_02 - 1 center + 2 open
        Add(configs, "Tiles_BasicCorner_02_A", O, W, C, O);  // B and F + L NO WALLS
        Add(configs, "Tiles_BasicCorner_02_B", O, O, C, W);  // B and F + R NO WALLS
        Add(configs, "Tiles_BasicCorner_02_C", C, W, O, O);  // F and L + B NO WALLS
        Add(configs, "Tiles_BasicCorner_02_D", C, O, O, W);  // F and R + B NO WALLS

        // BasicCorner_03 - 1 center + 2 open
        Add(configs, "Tiles_BasicCorner_03_A", O, C, W, O);  // R and F + L NO WALLS
        Add(configs, "Tiles_BasicCorner_03_B", O, O, W, C);  // L and F + R NO WALLS
        Add(configs, "Tiles_BasicCorner_03_C", W, C, O, O);  // R and L + B NO WALLS
        Add(configs, "Tiles_BasicCorner_03_D", W, O, O, C);  // L and R + B NO WALLS

        // BasicCorner_04 - 1 left/right + 2 open
        Add(configs, "Tiles_BasicCorner_04_A", O, W, L, O);  // B right -> B left (flipped for back)
        Add(configs, "Tiles_BasicCorner_04_B", O, O, R, W);  // B left -> B right (flipped for back)
        Add(configs, "Tiles_BasicCorner_04_C", R, W, O, O);  // F right and L + B NO WALLS
        Add(configs, "Tiles_BasicCorner_04_D", L, O, O, W);  // F left and R + B NO WALLS

        // BasicCorner_05 - 1 left/right + 2 open
        Add(configs, "Tiles_BasicCorner_05_A", O, R, W, O);  // R right and L + F NO WALLS
        Add(configs, "Tiles_BasicCorner_05_B", O, O, W, L);  // L left and F + R NO WALLS
        Add(configs, "Tiles_BasicCorner_05_C", W, L, O, O);  // R left and L + B NO WALLS
        Add(configs, "Tiles_BasicCorner_05_D", W, O, O, R);  // L right and R + B NO WALLS

        // BasicCorner_06 - TRANSITION tiles (2 center + 2 open)
        Add(configs, "Tiles_BasicCorner_06_A", O, C, C, O);  // R + B and L + F NO WALLS
        Add(configs, "Tiles_BasicCorner_06_B", O, O, C, C);  // L + B and F + R NO WALLS
        Add(configs, "Tiles_BasicCorner_06_C", C, C, O, O);  // F + R and L + B NO WALLS
        Add(configs, "Tiles_BasicCorner_06_D", C, O, O, C);  // Assuming pattern

        // BasicSide_01 - 3 open edges
        Add(configs, "Tiles_BasicSide_01_A", O, O, W, O);  // L + F + R NO WALLS
        Add(configs, "Tiles_BasicSide_01_B", O, W, O, O);  // L + F + B NO WALLS
        Add(configs, "Tiles_BasicSide_01_C", O, O, O, W);  // F + B + R NO WALLS
        Add(configs, "Tiles_BasicSide_01_D", W, O, O, O);  // L + B + R NO WALLS

        // BasicSide_02 - 1 center + 3 open
        Add(configs, "Tiles_BasicSide_02_A", O, O, C, O);  // B and L + F + R NO WALLS
        Add(configs, "Tiles_BasicSide_02_B", O, C, O, O);  // R and L + F + B NO WALLS
        Add(configs, "Tiles_BasicSide_02_C", O, O, O, C);  // L and B + F + R NO WALLS
        Add(configs, "Tiles_BasicSide_02_D", C, O, O, O);  // F and L + B + R NO WALLS

        // ══════════════════════════════════════════
        // T SERIES (left/right offset openings)
        // ══════════════════════════════════════════

        // T__ - 4-way with offsets
        Add(configs, "Tiles_T__01_A", L, C, R, C);  // F left + L + R + B left (flipped to B right)
        Add(configs, "Tiles_T__01_B", R, C, L, C);  // F right + L + R + B right (flipped to B left)
        Add(configs, "Tiles_T__02_A", C, R, C, L);  // F + L left + R right + B
        Add(configs, "Tiles_T__02_B", C, L, C, R);  // F + L right + R left + B
        Add(configs, "Tiles_T__03_A", L, C, R, C);  // Same as 01_A (flipped)
        Add(configs, "Tiles_T__03_B", R, C, L, C);  // Same as 01_B (flipped)
        Add(configs, "Tiles_T__04_A", C, R, C, L);  // Same as 02_A
        Add(configs, "Tiles_T__04_B", C, L, C, R);  // Same as 02_B

        // T_Side - 2-way with offsets
        Add(configs, "Tiles_T_Side_01_A", L, W, W, C);  // F left + L
        Add(configs, "Tiles_T_Side_01_B", R, C, W, W);  // F right + R
        Add(configs, "Tiles_T_Side_01_C", W, W, R, C);  // B left (flipped to B right) + L
        Add(configs, "Tiles_T_Side_01_D", W, C, L, W);  // B right (flipped to B left) + R

        Add(configs, "Tiles_T_Side_02_A", W, W, C, L);  // L left + B
        Add(configs, "Tiles_T_Side_02_B", C, W, W, R);  // L right + F
        Add(configs, "Tiles_T_Side_02_C", W, R, C, W);  // R right + B
        Add(configs, "Tiles_T_Side_02_D", C, L, W, W);  // F + R left

        Add(configs, "Tiles_T_Side_03_A", L, W, O, C);  // F left + L + B NO WALLS
        Add(configs, "Tiles_T_Side_03_B", R, C, O, W);  // F right + R + B NO WALLS
        Add(configs, "Tiles_T_Side_03_C", O, W, R, C);  // B left (flipped to B right) + L + F NO WALLS
        Add(configs, "Tiles_T_Side_03_D", O, C, L, W);  // B right (flipped to B left) + R + F NO WALLS

        Add(configs, "Tiles_T_Side_04_A", O, O, W, L);  // L left and F + R NO WALLS
        Add(configs, "Tiles_T_Side_04_B", W, O, O, R);  // L right and R + B NO WALLS
        Add(configs, "Tiles_T_Side_04_C", O, O, W, L);  // Same as A
        Add(configs, "Tiles_T_Side_04_D", W, L, O, O);  // R left and L + B NO WALLS

        // TCorner_01 series - single left/right openings on one edge
        Add(configs, "Tiles_TCorner_01_A", L, W, W, W);  // F left
        Add(configs, "Tiles_TCorner_01_B", R, W, W, W);  // F right
        Add(configs, "Tiles_TCorner_01_C", W, W, R, W);  // B left (flipped to B right)
        Add(configs, "Tiles_TCorner_01_D", W, W, L, W);  // B right (flipped to B left)

        // TCorner_02 series - single left/right openings on side edges
        Add(configs, "Tiles_TCorner_02_A", W, W, W, L);  // L left
        Add(configs, "Tiles_TCorner_02_B", W, W, W, R);  // L right
        Add(configs, "Tiles_TCorner_02_C", W, R, W, W);  // R right
        Add(configs, "Tiles_TCorner_02_D", W, L, W, W);  // R left

        // TCorner_03 series - one left/right opening + 2 open edges
        Add(configs, "Tiles_TCorner_03_A", L, O, O, W);  // F left and R + B NO WALLS
        Add(configs, "Tiles_TCorner_03_B", R, W, O, O);  // F right and L + B NO WALLS
        Add(configs, "Tiles_TCorner_03_C", O, O, R, W);  // B left (flipped to B right) and F + R NO WALLS
        Add(configs, "Tiles_TCorner_03_D", O, W, L, O);  // B right (flipped to B left) and L + F NO WALLS

        // TCorner_04 series - one left/right opening + 2 open edges (sides)
        Add(configs, "Tiles_TCorner_04_A", O, O, W, L);  // L left and F + R NO WALLS
        Add(configs, "Tiles_TCorner_04_B", W, O, O, R);  // L right and R + B NO WALLS
        Add(configs, "Tiles_TCorner_04_C", O, R, W, O);  // R right and F + L NO WALLS
        Add(configs, "Tiles_TCorner_04_D", W, L, O, O);  // R left and L + B NO WALLS

        // TCornerSide series - mixed left/right with center
        Add(configs, "Tiles_TCornerSide_01_A", L, W, R, C);  // F left + L + B left (flipped to B right)
        Add(configs, "Tiles_TCornerSide_01_B", R, C, L, W);  // F right + R + B right (flipped to B left)
        Add(configs, "Tiles_TCornerSide_02_A", W, R, C, L);  // L left + R right + B
        Add(configs, "Tiles_TCornerSide_02_B", C, L, W, R);  // F + L right + R left
        Add(configs, "Tiles_TCornerSide_03_A", L, O, R, C);  // F left + L + B left (flipped to B right) and R NO WALLS
        Add(configs, "Tiles_TCornerSide_03_B", R, C, L, O);  // F right + R + B right (flipped to B left) and L NO WALLS
        Add(configs, "Tiles_TCornerSide_04_A", O, R, C, L);  // L left + R right + B and F NO WALLS
        Add(configs, "Tiles_TCornerSide_04_B", C, L, O, R);  // L right + R Left + F and B NO WALLS

        // THalls series - open corridors
        Add(configs, "Tiles_THalls_01_A", R, W, L, W);  // HALL on right side and F + B NO WALLS
        Add(configs, "Tiles_THalls_01_B", L, W, R, W);  // HALL on left side and F + B NO WALLS
        Add(configs, "Tiles_THalls_01_C", W, R, W, L);  // HALL on back side and R + L NO WALLS
        Add(configs, "Tiles_THalls_01_D", W, L, W, R);  // HALL on front side and R + L NO WALLS

        // TStairs series - single left/right openings
        Add(configs, "Tiles_TStairs_01_A", R, W, W, W);  // F right
        Add(configs, "Tiles_TStairs_01_B", W, W, W, R);  // L right
        Add(configs, "Tiles_TStairs_01_C", W, R, W, W);  // R right
        Add(configs, "Tiles_TStairs_01_D", W, W, L, W);  // B left

        Add(configs, "Tiles_TStairs_02_A", L, W, W, W);  // F left
        Add(configs, "Tiles_TStairs_02_B", W, W, W, L);  // L left
        Add(configs, "Tiles_TStairs_02_C", W, L, W, W);  // R left
        Add(configs, "Tiles_TStairs_02_D", W, W, L, W);  // B right (flipped to B left)

        // Fill tile - all walls
        Add(configs, "Tiles_01_Fill", W, W, W, W);
    }

    static void Add(Dictionary<string, ProceduralDungeonGenerator.TileConfig> configs,
                    string name,
                    ProceduralDungeonGenerator.EdgeType north,
                    ProceduralDungeonGenerator.EdgeType east,
                    ProceduralDungeonGenerator.EdgeType south,
                    ProceduralDungeonGenerator.EdgeType west)
    {
        configs[name] = new ProceduralDungeonGenerator.TileConfig
        {
            tileName = name, north = north, east = east, south = south, west = west
        };
    }
}
