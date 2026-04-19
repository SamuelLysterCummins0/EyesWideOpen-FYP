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

    [Header("Pacer NPC")]
    [Tooltip("The Pacer NPC prefab (PacerNPC component required). Spawned from level 1 onward.")]
    public GameObject pacerPrefab;
    [Tooltip("How many Pacers to spawn per level index (index 0 = level 0, etc.). Level 0 typically 0.")]
    public int[] pacersPerLevel = { 0, 1, 2, 2 };



    private ProceduralDungeonGenerator dungeonGenerator;
    private List<NPCSpawnManager> levelSpawnManagers = new List<NPCSpawnManager>();

    // Track Pacer/Watcher instances per level so they can be respawned independently
    private readonly List<List<GameObject>> levelPacers   = new List<List<GameObject>>();
    
    // Cache spawn zones per level for respawn use
    private readonly List<List<Transform>> levelSpawnZones = new List<List<Transform>>();

    private void Awake()
    {
        dungeonGenerator = GetComponent<ProceduralDungeonGenerator>();
    }

    public void SetupLevel(GameObject levelParent, int levelIndex,
                          ProceduralDungeonGenerator.TileConfig[,] configs,
                          int dungeonWidth, int dungeonHeight, float tileSize, float levelHeight,
                          int connStartX = 0, int connStartZ = 0)
    {

        SetTilesLayer(levelParent, levelIndex);
        // 1. Bake NavMesh for this level
        BakeNavMesh(levelParent, levelIndex);

        // 2. Create spawn zones based on room tiles
        List<Transform> spawnZones = CreateSpawnZones(levelParent, levelIndex, configs,
                                                      dungeonWidth, dungeonHeight, tileSize, levelHeight,
                                                      connStartX, connStartZ);

        // 3. Setup NPC spawn manager (Weeping Angels)
        SetupSpawnManager(levelParent, levelIndex, spawnZones);

        // 4. Spawn Pacers and Watchers for this level
        SpawnPacersAndWatchers(levelParent, levelIndex, spawnZones);
    }

    private void BakeNavMesh(GameObject levelParent, int levelIndex)
    {
        // ── Exclude WallBarrier objects from the bake ────────────────────────────
        // PhysicsColliders geometry mode works correctly in builds (unlike RenderMeshes,
        // which requires every mesh to have "Read/Write Enabled" in its import settings —
        // a flag that is off by default and causes ALL tile meshes to be silently skipped
        // in a build, producing a completely empty NavMesh).
        //
        // The downside of PhysicsColliders is that DungeonWallSealer's invisible BoxCollider
        // barriers (named "WallBarrier_*") sit exactly at tile seams and get included in the
        // bake, carving the walkable area at every connection point and breaking NPC pathfinding.
        // We temporarily mark each barrier with NavMeshModifier.ignoreFromBuild = true so the
        // bake skips them, then remove the modifier immediately afterwards.
        var addedModifiers = new System.Collections.Generic.List<NavMeshModifier>();
        foreach (Transform t in levelParent.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (t.name.StartsWith("WallBarrier"))
            {
                NavMeshModifier mod = t.gameObject.AddComponent<NavMeshModifier>();
                mod.ignoreFromBuild = true;
                addedModifiers.Add(mod);
            }
        }

        NavMeshSurface navSurface = levelParent.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
        navSurface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
        navSurface.BuildNavMesh();

        // Clean up temporary modifiers — they're only needed during BuildNavMesh().
        foreach (NavMeshModifier mod in addedModifiers)
            Destroy(mod);

        Debug.Log($"Level {levelIndex}: NavMesh baked successfully ({addedModifiers.Count} WallBarriers excluded)");
    }

    private List<Transform> CreateSpawnZones(GameObject levelParent, int levelIndex,
                                            ProceduralDungeonGenerator.TileConfig[,] configs,
                                            int dungeonWidth, int dungeonHeight,
                                            float tileSize, float levelHeight,
                                            int connStartX, int connStartZ)
    {
        List<Transform> zones = new List<Transform>();
        float levelY = levelIndex * -levelHeight;

        // --- Build reachable set ---
        // Only create spawn zones on tiles reachable from connStart so NPCs never
        // appear in pockets that the player cannot actually navigate to.
        HashSet<Vector2Int> reachable = dungeonGenerator != null
            ? dungeonGenerator.GetLevelReachable(levelIndex)
            : new HashSet<Vector2Int>();

        // --- Build excluded grid positions from all protected rooms ---
        // Any tile used as a safe room, spawn room, computer room, or hidden room
        // is excluded, plus its immediate neighbours (1-tile buffer).
        HashSet<Vector2Int> excludedTiles = new HashSet<Vector2Int>();

        // Helper: mark a world-space centre and its 4 neighbours as excluded
        System.Action<Vector3> excludeCenter = (Vector3 worldCenter) =>
        {
            int gx = Mathf.RoundToInt(worldCenter.x / tileSize);
            int gz = Mathf.RoundToInt(worldCenter.z / tileSize);
            excludedTiles.Add(new Vector2Int(gx, gz));
            excludedTiles.Add(new Vector2Int(gx + 1, gz));
            excludedTiles.Add(new Vector2Int(gx - 1, gz));
            excludedTiles.Add(new Vector2Int(gx, gz + 1));
            excludedTiles.Add(new Vector2Int(gx, gz - 1));
        };

        SafeRoomSetup safeRoomSetup = FindObjectOfType<SafeRoomSetup>();
        if (safeRoomSetup != null)
            foreach (Vector3 c in safeRoomSetup.GetSafeRoomCenters())   excludeCenter(c);

        SpawnRoomSetup spawnRoomSetup = FindObjectOfType<SpawnRoomSetup>();
        if (spawnRoomSetup != null)
            foreach (Vector3 c in spawnRoomSetup.GetSpawnRoomPositions()) excludeCenter(c);

        ComputerRoomSetup computerRoomSetup = FindObjectOfType<ComputerRoomSetup>();
        if (computerRoomSetup != null)
            foreach (Vector3 c in computerRoomSetup.GetRoomCenters())    excludeCenter(c);

        HiddenRoomSetup hiddenRoomSetup = FindObjectOfType<HiddenRoomSetup>();
        if (hiddenRoomSetup != null)
            foreach (Vector3 c in hiddenRoomSetup.GetRoomCenters())      excludeCenter(c);

        // --- Create zones ---
        for (int x = 0; x < dungeonWidth; x++)
        {
            for (int z = 0; z < dungeonHeight; z++)
            {
                if (configs[x, z] == null) continue;

                ProceduralDungeonGenerator.TileConfig config = configs[x, z];

                // Only spawn in room tiles (Open or Wall edges only), not perimeter or fully-open tiles
                if (!config.IsRoomTile() || config.IsFullyOpen()) continue;
                bool isEdge = x == 0 || x == dungeonWidth - 1 || z == 0 || z == dungeonHeight - 1;
                if (isEdge) continue;

                Vector2Int gridPos = new Vector2Int(x, z);

                // Skip isolated tiles — NPCs here would be unreachable
                if (reachable.Count > 0 && !reachable.Contains(gridPos)) continue;

                // Skip protected room tiles and their immediate neighbours
                if (excludedTiles.Contains(gridPos)) continue;

                GameObject zoneObj = new GameObject($"SpawnZone_{x}_{z}");
                zoneObj.transform.parent = levelParent.transform;
                zoneObj.transform.position = new Vector3(x * tileSize, levelY + 0.5f, z * tileSize);
                zoneObj.transform.localScale = Vector3.one * spawnZoneSize;
                zones.Add(zoneObj.transform);
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

        Debug.Log($"Level {levelIndex}: Created {zones.Count} spawn zones ({excludedTiles.Count / 5} protected rooms excluded)");
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

        // Cache zone list so Pacer/Watcher respawn can reuse it
        while (levelSpawnZones.Count <= levelIndex)
            levelSpawnZones.Add(new List<Transform>());
        levelSpawnZones[levelIndex] = new List<Transform>(spawnZones);

        Debug.Log($"Level {levelIndex}: Spawn manager configured with {wave.numberOfNPCsToSpawn} NPCs in {spawnZones.Count} zones");
    }

    /// <summary>
    /// Instantiates Pacer and Watcher NPCs for a level, placed at random valid NavMesh positions
    /// sampled from existing spawn zones. Pacers start at level 1, Watchers at level 2.
    /// </summary>
    private void SpawnPacersAndWatchers(GameObject levelParent, int levelIndex, List<Transform> spawnZones)
    {
        while (levelPacers.Count   <= levelIndex) levelPacers.Add(new List<GameObject>());
        

        int pacerCount   = levelIndex < pacersPerLevel.Length   ? pacersPerLevel[levelIndex]   : 0;
        

        if (pacerCount > 0 && pacerPrefab == null)
            Debug.LogWarning($"[DungeonNavMeshSetup] pacerPrefab not assigned — skipping {pacerCount} Pacer(s) for level {levelIndex}.");

        

        Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;

        SpawnNPCGroup(pacerPrefab,   pacerCount,   levelParent, levelIndex, spawnZones, player, levelPacers[levelIndex],   "Pacer");
        
    }

    private void SpawnNPCGroup(GameObject prefab, int count, GameObject levelParent, int levelIndex,
                               List<Transform> spawnZones, Transform player,
                               List<GameObject> trackedList, string label)
    {
        if (prefab == null || count <= 0 || spawnZones.Count == 0) return;

        // Shuffle a copy of zone indices so we don't always pick the first zones
        List<int> indices = new List<int>();
        for (int i = 0; i < spawnZones.Count; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
        }

        int spawned = 0;
        foreach (int idx in indices)
        {
            if (spawned >= count) break;

            Vector3 zonePos = spawnZones[idx].position;

            // Skip if too close to player
            if (player != null && Vector3.Distance(zonePos, player.position) < minDistanceFromPlayer)
                continue;

            // Sample a valid NavMesh position near the zone centre
            if (!UnityEngine.AI.NavMesh.SamplePosition(zonePos, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                continue;

            Quaternion randomRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject npcObj = Instantiate(prefab, hit.position, randomRot, levelParent.transform);
            npcObj.name = $"{label}_L{levelIndex}_{spawned}";
            trackedList.Add(npcObj);

            // Tell the NPC which level it's on so it uses the right speed/rate values
            npcObj.GetComponent<PacerNPC>()?.SetLevel(levelIndex);
            

            spawned++;
        }

        Debug.Log($"[DungeonNavMeshSetup] Level {levelIndex}: spawned {spawned}/{count} {label}(s).");
    }

    public void ClearAllSpawns()
    {
        foreach (NPCSpawnManager manager in levelSpawnManagers)
        {
            if (manager != null)
                manager.ClearSpawns();
        }
        levelSpawnManagers.Clear();

        foreach (var list in levelPacers)
            foreach (GameObject g in list) if (g != null) Destroy(g);
        levelPacers.Clear();

        

        levelSpawnZones.Clear();
    }

    // Re-randomises NPC positions for a single level — called on player respawn.
    public void RespawnNPCsForLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levelSpawnManagers.Count)
        {
            Debug.LogWarning($"DungeonNavMeshSetup: No spawn manager for level {levelIndex}");
            return;
        }

        // Respawn Weeping Angels via SpawnManager
        NPCSpawnManager manager = levelSpawnManagers[levelIndex];
        if (manager != null)
            manager.RespawnAll();

        // Reposition Pacers
        if (levelIndex < levelPacers.Count && levelIndex < levelSpawnZones.Count)
            RespawnNPCList(levelPacers[levelIndex], levelSpawnZones[levelIndex]);

        
    }

    /// <summary>Teleports each NPC in the list to a new random valid NavMesh position.</summary>
    private void RespawnNPCList(List<GameObject> npcs, List<Transform> zones)
    {
        if (zones == null || zones.Count == 0) return;
        Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;

        foreach (GameObject npc in npcs)
        {
            if (npc == null) continue;

            // Pick a random zone that is far enough from the player
            List<int> indices = new List<int>();
            for (int i = 0; i < zones.Count; i++) indices.Add(i);
            // Simple shuffle
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
            }

            foreach (int idx in indices)
            {
                Vector3 zonePos = zones[idx].position;
                if (player != null && Vector3.Distance(zonePos, player.position) < minDistanceFromPlayer)
                    continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(zonePos, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                    continue;

                // Warp the NavMeshAgent to the new position
                var agent = npc.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null)
                {
                    agent.Warp(hit.position);
                }
                else
                {
                    npc.transform.position = hit.position;
                }
                break;
            }
        }
    }

    // Returns the NPCSpawnManager for a given level — used by RoomNPCShuffle.
    public NPCSpawnManager GetSpawnManagerForLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levelSpawnManagers.Count) return null;
        return levelSpawnManagers[levelIndex];
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