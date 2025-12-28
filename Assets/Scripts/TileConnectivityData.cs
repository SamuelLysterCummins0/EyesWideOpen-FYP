using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Defines the connectivity data for dungeon tiles based on their wall/opening configuration
/// F = Front (+Z North), B = Back (-Z South), L = Left (-X West), R = Right (+X East)
/// </summary>
public class TileConnectivityData : ScriptableObject
{
    public enum EdgeType
    {
        SolidWall,          // Complete wall, no opening
        CenterOpening,      // Opening in center of wall
        LeftOpening,        // Opening on left side of wall
        RightOpening,       // Opening on right side of wall
        NoWall              // No wall at all (completely open)
    }

    [System.Serializable]
    public class TileDefinition
    {
        public string tileName;
        public GameObject prefab;

        // Edge connectivity for each direction
        public EdgeType frontEdge;   // +Z (North)
        public EdgeType backEdge;    // -Z (South)
        public EdgeType leftEdge;    // -X (West)
        public EdgeType rightEdge;   // +X (East)

        // Special properties
        public bool hasUpperLevel;
        public bool hasHoleInFloor;
        public bool hasHoleInCeiling;

        // Weight for random selection (higher = more likely)
        public float weight = 1f;

        public bool CanConnectTo(TileDefinition other, Direction direction)
        {
            EdgeType myEdge = GetEdgeForDirection(direction);
            EdgeType theirEdge = other.GetEdgeForDirection(GetOppositeDirection(direction));

            return EdgesAreCompatible(myEdge, theirEdge);
        }

        public EdgeType GetEdgeForDirection(Direction dir)
        {
            switch (dir)
            {
                case Direction.North: return frontEdge;
                case Direction.South: return backEdge;
                case Direction.West: return leftEdge;
                case Direction.East: return rightEdge;
                default: return EdgeType.SolidWall;
            }
        }

        private Direction GetOppositeDirection(Direction dir)
        {
            switch (dir)
            {
                case Direction.North: return Direction.South;
                case Direction.South: return Direction.North;
                case Direction.East: return Direction.West;
                case Direction.West: return Direction.East;
                default: return Direction.North;
            }
        }

        private bool EdgesAreCompatible(EdgeType edge1, EdgeType edge2)
        {
            // Both must be openings of the same type, or both NoWall
            if (edge1 == EdgeType.NoWall && edge2 == EdgeType.NoWall)
                return true;

            if (edge1 == EdgeType.CenterOpening && edge2 == EdgeType.CenterOpening)
                return true;

            // Left opening connects to right opening (and vice versa)
            if (edge1 == EdgeType.LeftOpening && edge2 == EdgeType.RightOpening)
                return true;
            if (edge1 == EdgeType.RightOpening && edge2 == EdgeType.LeftOpening)
                return true;

            // NoWall can connect to any opening type
            if (edge1 == EdgeType.NoWall && IsOpening(edge2))
                return true;
            if (edge2 == EdgeType.NoWall && IsOpening(edge1))
                return true;

            // Solid walls don't connect (would show see-through backs)
            return false;
        }

        private bool IsOpening(EdgeType edge)
        {
            return edge == EdgeType.CenterOpening ||
                   edge == EdgeType.LeftOpening ||
                   edge == EdgeType.RightOpening ||
                   edge == EdgeType.NoWall;
        }
    }

    public enum Direction
    {
        North,  // +Z
        East,   // +X
        South,  // -Z
        West    // -X
    }

    public List<TileDefinition> allTiles = new List<TileDefinition>();

    // Helper method to create tile definitions programmatically
    public static TileDefinition CreateTile(string name, GameObject prefab,
        EdgeType front, EdgeType back, EdgeType left, EdgeType right,
        float weight = 1f)
    {
        return new TileDefinition
        {
            tileName = name,
            prefab = prefab,
            frontEdge = front,
            backEdge = back,
            leftEdge = left,
            rightEdge = right,
            weight = weight
        };
    }
}