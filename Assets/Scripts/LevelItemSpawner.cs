using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a configurable list of items at each level's player spawn point
/// (the stairway entrance tile the player steps into on arrival).
///
/// This replaces the old hardcoded flashlight spawn in BatterySpawnSetup.
/// Add one entry per item — batteries are still handled by BatterySpawnSetup.
///
/// Setup:
///   1. Add this component to the same GameObject as ProceduralDungeonGenerator.
///   2. Press + in the Items list for each item to spawn.
///   3. For the flashlight: Prefab = flashlight prefab, Level Index = 1.
///   4. Add more entries for any future items (keys, notes, etc.).
/// </summary>
public class LevelItemSpawner : MonoBehaviour
{
    [System.Serializable]
    public class ItemSpawnEntry
    {
        [Tooltip("Prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("Which dungeon level to place this on (0 = first level, 1 = second, etc.).")]
        public int levelIndex = 1;

        [Tooltip("Height above the floor surface.")]
        public float floorHeightOffset = 0.1f;

        [Tooltip("Spawn the item facing a random direction.")]
        public bool randomYRotation = false;

        [Tooltip("Optional name override. Leave empty to use the prefab name.")]
        public string spawnName = "";
    }

    [Tooltip("One entry per item to place in the dungeon. Each spawns at its level's spawn point.")]
    public List<ItemSpawnEntry> items = new List<ItemSpawnEntry>();

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    /// <summary>Called by ProceduralDungeonGenerator after all rooms are set up for a level.</summary>
    public void SetupLevel(
        ProceduralDungeonGenerator gen,
        int                        levelIndex,
        GameObject                 levelParent,
        SpawnRoomSetup             spawnRoomSetup    = null,
        SafeRoomSetup              safeRoomSetup     = null,
        HiddenRoomSetup            hiddenRoomSetup   = null,
        ComputerRoomSetup          computerRoomSetup = null)
    {
        if (items == null) return;

        float levelY = levelIndex * -gen.LevelHeight;

        foreach (ItemSpawnEntry entry in items)
        {
            if (entry.prefab == null || entry.levelIndex != levelIndex) continue;

            if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(levelIndex))
            {
                Debug.LogWarning($"[LevelItemSpawner] No spawn point for level {levelIndex} " +
                                 $"— cannot place '{entry.prefab.name}'.");
                continue;
            }

            Vector3 sp       = spawnRoomSetup.GetSpawnPoint(levelIndex);
            Vector3 spawnPos = new Vector3(sp.x, levelY + entry.floorHeightOffset, sp.z);

            Quaternion rot = entry.randomYRotation
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : Quaternion.identity;

            GameObject obj = Instantiate(entry.prefab, spawnPos, rot, levelParent.transform);
            obj.name = !string.IsNullOrEmpty(entry.spawnName) ? entry.spawnName : entry.prefab.name;

            spawnedObjects.Add(obj);
            Debug.Log($"[LevelItemSpawner] Spawned '{obj.name}' at {spawnPos} (level {levelIndex}).");
        }
    }

    /// <summary>Called by ProceduralDungeonGenerator.ClearDungeon before regeneration.</summary>
    public void ClearAll()
    {
        spawnedObjects.Clear(); // Objects are parented to levelParent and destroyed with it.
    }
}
