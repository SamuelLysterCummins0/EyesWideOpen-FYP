using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class DungeonNavMeshSetup : MonoBehaviour
{
    [Header("NPC Spawning")]
    public GameObject npcSpawnManagerPrefab;
    public GameObject npcPrefab;
    public GameObject applePrefab;

    [Header("Spawn Settings")]
    public int baseNPCsPerLevel = 2; // Level 0 gets this many
    public float spawnZoneSize = 4f;
    public float minDistanceFromPlayer = 15f;
    public float minDistanceBetweenNPCs = 10f;

    private ProceduralDungeonGenerator dungeonGenerator;
    private List<NPCSpawnManager> levelSpawnManagers = new List<NPCSpawnManager>();

    private void Awake()
    {
        dungeonGenerator = GetComponent<ProceduralDungeonGenerator>();
    }

    public void SetupLevel(GameObject levelParent, int levelIndex,
                          ProceduralDungeonGenerator.TileConfig[,] configs,
                          int dungeonWidth, int dungeonHeight, float tileSize, float levelHeight)
    {

        SetTilesLayer(levelParent, levelIndex);
        // 1. Bake NavMesh for this level
        BakeNavMesh(levelParent, levelIndex);

        // 2. Create spawn zones based on room tiles
        List<Transform> spawnZones = CreateSpawnZones(levelParent, levelIndex, configs,
                                                      dungeonWidth, dungeonHeight, tileSize, levelHeight);

        // 3. Setup NPC spawn manager
        SetupSpawnManager(levelParent, levelIndex, spawnZones);
    }

    private void BakeNavMesh(GameObject levelParent, int levelIndex)
    {
        NavMeshSurface navSurface = levelParent.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
        // Default geometry source (RenderMeshes) — do NOT use PhysicsColliders here.
        // The WallBarrier objects from DungeonWallSealer are collider-only with no renderer,
        // so PhysicsColliders mode picks them up and carves the NavMesh exactly at tile seams,
        // breaking connectivity between tiles. RenderMeshes ignores them entirely.
        navSurface.BuildNavMesh();
        Debug.Log($"Level {levelIndex}: NavMesh baked successfully");
    }

    private List<Transform> CreateSpawnZones(GameObject levelParent, int levelIndex,
                                            ProceduralDungeonGenerator.TileConfig[,] configs,
                                            int dungeonWidth, int dungeonHeight,
                                            float tileSize, float levelHeight)
    {
        List<Transform> zones = new List<Transform>();
        float levelY = levelIndex * -levelHeight;

        // Create spawn zones at room tiles (safer for NPC navigation)
        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (configs[x, z] == null) continue;

                ProceduralDungeonGenerator.TileConfig config = configs[x, z];

                // Only spawn in room tiles (Open or Wall edges only)
                if (config.IsRoomTile() && !config.IsFullyOpen())
                {
                    // Skip perimeter tiles
                    bool isEdge = x == 0 || x == dungeonWidth - 1 || z == 0 || z == dungeonHeight - 1;
                    if (isEdge) continue;

                    // Create spawn zone cube
                    GameObject zoneObj = new GameObject($"SpawnZone_{x}_{z}");
                    zoneObj.transform.parent = levelParent.transform;
                    zoneObj.transform.position = new Vector3(x * tileSize, levelY + 0.5f, z * tileSize);
                    zoneObj.transform.localScale = Vector3.one * spawnZoneSize;

                    zones.Add(zoneObj.transform);
                }
            }
        }

        // Fallback: if no zones created, create central zone
        if (zones.Count == 0)
        {
            GameObject centerZone = new GameObject("SpawnZone_Center");
            centerZone.transform.parent = levelParent.transform;
            centerZone.transform.position = new Vector3((dungeonWidth * tileSize) / 2, levelY + 0.5f, (dungeonHeight * tileSize) / 2);
            centerZone.transform.localScale = new Vector3(dungeonWidth * tileSize * 0.4f, 1f, dungeonHeight * tileSize * 0.4f);
            zones.Add(centerZone.transform);
            Debug.LogWarning($"Level {levelIndex}: No valid spawn zones found, created center zone");
        }

        Debug.Log($"Level {levelIndex}: Created {zones.Count} spawn zones");
        return zones;
    }

    private void SetupSpawnManager(GameObject levelParent, int levelIndex, List<Transform> spawnZones)
    {
        if (npcSpawnManagerPrefab == null)
        {
            Debug.LogWarning("NPC Spawn Manager Prefab not assigned - skipping NPC setup");
            return;
        }

        // Instantiate spawn manager for this level
        GameObject managerObj = Instantiate(npcSpawnManagerPrefab, levelParent.transform);
        managerObj.name = $"NPCSpawnManager_Level_{levelIndex}";

        NPCSpawnManager spawnManager = managerObj.GetComponent<NPCSpawnManager>();
        if (spawnManager == null)
        {
            spawnManager = managerObj.AddComponent<NPCSpawnManager>();
        }

        // Configure spawn manager
        spawnManager.npcPrefab = npcPrefab;
        spawnManager.applePrefab = applePrefab;
        spawnManager.spawnZones = spawnZones;
        spawnManager.obstaclesLayer = LayerMask.GetMask("Obstacle");
        spawnManager.minDistanceFromPlayer = minDistanceFromPlayer;
        spawnManager.minDistanceBetweenNPCs = minDistanceBetweenNPCs;

        // Find player
        Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player != null)
        {
            spawnManager.player = player;
        }

        // Setup waves - more NPCs on deeper levels
        spawnManager.spawnWaves = new List<NPCSpawnManager.SpawnWave>();
        NPCSpawnManager.SpawnWave wave = new NPCSpawnManager.SpawnWave();
        wave.waveName = $"Level {levelIndex} Initial Spawn";
        wave.numberOfNPCsToSpawn = baseNPCsPerLevel + levelIndex; // Level 0=2, Level 1=3, Level 2=4, etc.
        wave.numberOfApplesToSpawn = 1;
        spawnManager.spawnWaves.Add(wave);

        levelSpawnManagers.Add(spawnManager);

        Debug.Log($"Level {levelIndex}: Spawn manager configured with {wave.numberOfNPCsToSpawn} NPCs in {spawnZones.Count} zones");
    }

    public void ClearAllSpawns()
    {
        foreach (NPCSpawnManager manager in levelSpawnManagers)
        {
            if (manager != null)
                manager.ClearSpawns();
        }
        levelSpawnManagers.Clear();
    }

    // Re-randomises NPC positions for a single level — called on player respawn.
    public void RespawnNPCsForLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levelSpawnManagers.Count)
        {
            Debug.LogWarning($"DungeonNavMeshSetup: No spawn manager for level {levelIndex}");
            return;
        }

        NPCSpawnManager manager = levelSpawnManagers[levelIndex];
        if (manager != null)
            manager.RespawnAll();
    }

    private void SetTilesLayer(GameObject levelParent, int levelIndex)
    {
        // Set all tiles to Obstacles layer so NPCs can detect them
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer == -1)
        {
            Debug.LogError("'Obstacles' layer not found! Create it in Tags & Layers");
            return;
        }

        MeshRenderer[] renderers = levelParent.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.gameObject.layer = obstacleLayer;
        }

        Debug.Log($"Level {levelIndex}: Set {renderers.Length} objects to Obstacles layer");
    }

}