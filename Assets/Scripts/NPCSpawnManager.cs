using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

public class NPCSpawnManager : MonoBehaviour
{
    [System.Serializable]
    public class SpawnWave
    {
        public string waveName = "Wave";
        public int numberOfNPCsToSpawn = 1;
        public int numberOfApplesToSpawn = 1;
    }

    public GameObject npcPrefab;
    public GameObject applePrefab;
    public Transform player;
    public List<SpawnWave> spawnWaves;
    public LayerMask obstaclesLayer; // Make it public, not private

    // Spawn zone settings
    public List<Transform> spawnZones = new List<Transform>();
    public float minDistanceFromPlayer = 15f;
    public float minDistanceBetweenNPCs = 10f;
    public bool showSpawnZones = true;

    [Tooltip("NPCs cannot spawn within this radius of any safe room or spawn room entrance tile.")]
    public float safeRoomExclusionRadius = 8f;
    
    private List<GameObject> activeNPCs = new List<GameObject>();
    private List<GameObject> activeApples = new List<GameObject>();
    private Camera playerCamera;
    private SafeRoomSetup safeRoomSetup;
    private SpawnRoomSetup spawnRoomSetup;
    private ComputerRoomSetup computerRoomSetup;
    private HiddenRoomSetup hiddenRoomSetup;
    private DetonationRoomSetup detonationRoomSetup;
    private DungeonLevelVisibility dungeonLevelVisibility;

    // Locker exclusion zones — registered by LockerSetup during dungeon generation
    private readonly List<(Vector3 center, float radius)> lockerExclusions =
        new List<(Vector3 center, float radius)>();

    /// <summary>Called by LockerSetup for each spawned locker to prevent NPC spawning on top of them.</summary>
    public void RegisterLockerCenter(Vector3 center, float radius) =>
        lockerExclusions.Add((center, radius));

    void OnDrawGizmos()
    {
        if (showSpawnZones)
        {
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            foreach (Transform zone in spawnZones)
            {
                if (zone != null)
                {
                    Gizmos.matrix = zone.localToWorldMatrix;
                    Gizmos.DrawCube(Vector3.zero, Vector3.one);
                }
            }
        }
    }

    void Start()
    {
        playerCamera = Camera.main;
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player")?.transform;

        safeRoomSetup         = FindObjectOfType<SafeRoomSetup>();
        spawnRoomSetup        = FindObjectOfType<SpawnRoomSetup>();
        computerRoomSetup     = FindObjectOfType<ComputerRoomSetup>();
        hiddenRoomSetup       = HiddenRoomSetup.Instance != null ? HiddenRoomSetup.Instance : FindObjectOfType<HiddenRoomSetup>();
        detonationRoomSetup   = DetonationRoomSetup.Instance != null ? DetonationRoomSetup.Instance : FindObjectOfType<DetonationRoomSetup>();
        dungeonLevelVisibility = FindObjectOfType<DungeonLevelVisibility>();

        Debug.Log($"{name}: NPCSpawnManager started with {spawnZones.Count} spawn zones");

        // Only subscribe to car parts if we're using that system
        // CarPartsCollect.OnCarPartCollected += HandleCarPartCollected;

        // Spawn initial wave
        if (spawnWaves != null && spawnWaves.Count > 0)
        {
            SpawnWaveOfNPCs(0);
        }
    }

    private void SpawnWaveOfNPCs(int waveIndex)
    {
        if (waveIndex >= 0 && waveIndex < spawnWaves.Count)
        {
            SpawnWave wave = spawnWaves[waveIndex];
            for (int i = 0; i < wave.numberOfNPCsToSpawn; i++)
            {
                SpawnSingleNPC();
            }
            for (int i = 0; i < wave.numberOfApplesToSpawn; i++)
            {
                SpawnSingleApple();
            }
        }
    }

    private void SpawnSingleNPC()
    {
        Vector3 spawnPosition = GetValidSpawnPosition();
        if (spawnPosition != Vector3.zero)
        {
            Quaternion randomRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject npc = Instantiate(npcPrefab, spawnPosition, randomRot);
            ParentToLevel(npc, spawnPosition);
            SetupNPC(npc);
            activeNPCs.Add(npc);
        }
    }

    private void SpawnSingleApple()
    {
        Vector3 spawnPosition = GetValidSpawnPosition();
        if (spawnPosition != Vector3.zero)
        {
            GameObject apple = Instantiate(applePrefab, spawnPosition, Quaternion.identity);
            ParentToLevel(apple, spawnPosition);
            activeApples.Add(apple);
        }
    }

    /// <summary>
    /// Parents a spawned object to the dungeon level parent that matches its Y position.
    /// This ensures it is deactivated/activated with the level when it is hidden or shown,
    /// preventing objects and NPCs from falling or persisting when a level is toggled off.
    /// </summary>
    private void ParentToLevel(GameObject obj, Vector3 worldPosition)
    {
        if (dungeonLevelVisibility == null) return;

        float lh = dungeonLevelVisibility.LevelHeight;
        if (lh <= 0f) return;

        int levelIndex = Mathf.Max(0, Mathf.RoundToInt(-worldPosition.y / lh));
        GameObject levelParent = dungeonLevelVisibility.GetLevelParent(levelIndex);
        if (levelParent != null)
            obj.transform.SetParent(levelParent.transform, worldPositionStays: true);
    }

    private void SetupNPC(GameObject npc)
    {
        if (npc == null) return;

        NPCMovement npcMovement = npc.GetComponent<NPCMovement>();
        if (npcMovement == null)
        {
            Debug.LogError($"NPCMovement component missing on spawned NPC!");
            return;
        }

        if (npc.GetComponent<NavMeshAgent>() == null)
        {
            NavMeshAgent agent = npc.AddComponent<NavMeshAgent>();
            agent.radius = 0.5f;
            agent.height = 2f;
            agent.baseOffset = 0f;
            agent.speed = npcMovement.speed;
            agent.stoppingDistance = 0.8f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }

        if (npc.GetComponent<AudioSource>() == null)
        {
            AudioSource audio = npc.AddComponent<AudioSource>();
        }

        if (npc.GetComponent<Collider>() == null)
        {
            CapsuleCollider capsule = npc.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = 0.5f;
            capsule.height = 2f;
            capsule.center = new Vector3(0, 1f, 0);
        }
        else
        {
            Collider col = npc.GetComponent<Collider>();
            col.isTrigger = true;
        }

        // Assign basic references
        npcMovement.player = player;
        npcMovement.gameManager = GameManager.Instance;
        npcMovement.cameraControl = FindObjectOfType<CameraControl>();
        npcMovement.agent = npc.GetComponent<NavMeshAgent>();
        npcMovement.jumpscareAudio = npc.GetComponent<AudioSource>();

        // CRITICAL FIX: Set obstacles layer for raycasting
        npcMovement.obstaclesLayer = obstaclesLayer;

        // CRITICAL FIX: Auto-find blink detector and vignette
        if (npcMovement.blinkDetector == null)
        {
            npcMovement.blinkDetector = FindObjectOfType<BlinkDetector>();
        }

        if (npcMovement.vignetteController == null)
        {
            npcMovement.vignetteController = FindObjectOfType<BlinkVignetteController>();
        }

        Debug.Log($"NPC Setup Complete - ObstaclesLayer: {npcMovement.obstaclesLayer.value}, BlinkDetector: {npcMovement.blinkDetector != null}, Vignette: {npcMovement.vignetteController != null}");
    }

    private Vector3 GetValidSpawnPosition()
    {
        if (spawnZones == null || spawnZones.Count == 0) return Vector3.zero;

        for (int attempts = 0; attempts < 30; attempts++)
        {
            Transform zone = spawnZones[Random.Range(0, spawnZones.Count)];
            if (zone == null) continue;

            Vector3 randomPoint = new Vector3(
                Random.Range(-0.5f, 0.5f),
                0,
                Random.Range(-0.5f, 0.5f)
            );

            Vector3 worldPoint = zone.TransformPoint(randomPoint);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(worldPoint, out hit, 2.0f, NavMesh.AllAreas))
            {
                Vector3 finalPosition = hit.position;
                
                if (IsValidSpawnPosition(finalPosition))
                {
                    return finalPosition;
                }
            }
        }
        return Vector3.zero;
    }

    private bool IsValidSpawnPosition(Vector3 position)
    {
        return Vector3.Distance(position, player.position) >= minDistanceFromPlayer
            && !IsVisibleToPlayer(position)
            && IsFarEnoughFromOtherNPCs(position)
            && IsFarEnoughFromSafeRooms(position);
    }

    // Checks that the position is outside all protected room exclusion zones
    // (safe rooms, spawn rooms, computer rooms, and hidden rooms).
    private bool IsFarEnoughFromSafeRooms(Vector3 position)
    {
        if (safeRoomSetup != null)
            foreach (Vector3 center in safeRoomSetup.GetSafeRoomCenters())
                if (Vector3.Distance(position, center) < safeRoomExclusionRadius) return false;

        if (spawnRoomSetup != null)
            foreach (Vector3 center in spawnRoomSetup.GetSpawnRoomPositions())
                if (Vector3.Distance(position, center) < safeRoomExclusionRadius) return false;

        if (computerRoomSetup != null)
            foreach (Vector3 center in computerRoomSetup.GetRoomCenters())
                if (Vector3.Distance(position, center) < safeRoomExclusionRadius) return false;

        if (hiddenRoomSetup != null)
            foreach (Vector3 center in hiddenRoomSetup.GetRoomCenters())
                if (Vector3.Distance(position, center) < safeRoomExclusionRadius) return false;

        // Detonation room exclusion — uses a larger radius since the room occupies 2×2 tiles
        if (detonationRoomSetup != null)
            foreach (Vector3 center in detonationRoomSetup.GetRoomCenters())
                if (Vector3.Distance(position, center) < safeRoomExclusionRadius * 1.5f) return false;

        // Locker exclusion zones
        foreach (var (center, radius) in lockerExclusions)
            if (Vector3.Distance(position, center) < radius) return false;

        return true;
    }

    private bool IsVisibleToPlayer(Vector3 position)
    {
        // Null camera or disabled camera (e.g. player inside a locker) = not visible
        if (playerCamera == null || !playerCamera.enabled) return false;

        Vector3 directionToPlayer = (player.position - position).normalized;
        float angle = Vector3.Angle(-directionToPlayer, playerCamera.transform.forward);
        
        if (angle > 60f) return false;

        RaycastHit hit;
        if (Physics.Raycast(position, directionToPlayer, out hit, 100f, obstaclesLayer))
        {
            if (hit.transform != player.transform)
                return false;
        }
        
        return true;
    }

    private bool IsFarEnoughFromOtherNPCs(Vector3 position)
    {
        foreach (GameObject npc in activeNPCs)
        {
            if (npc != null && Vector3.Distance(position, npc.transform.position) < minDistanceBetweenNPCs)
            {
                return false;
            }
        }
        return true;
    }

    public void RemoveNPC(GameObject npc)
    {
        if (activeNPCs.Contains(npc))
        {
            activeNPCs.Remove(npc);
        }
    }

    public void RemoveApple(GameObject apple)
    {
        if (activeApples.Contains(apple))
        {
            activeApples.Remove(apple);
        }
    }

    // Destroys all active NPCs and apples for this level.
    public void ClearSpawns()
    {
        foreach (var npc in activeNPCs)
        {
            if (npc != null)
                Destroy(npc);
        }
        activeNPCs.Clear();

        foreach (var apple in activeApples)
        {
            if (apple != null)
                Destroy(apple);
        }
        activeApples.Clear();

    }

    // Clears all current NPCs then re-randomises their positions across the spawn zones.
    // Called by GameManager on respawn for the current level.
    public void RespawnAll()
    {
        ClearSpawns();
        if (spawnWaves != null && spawnWaves.Count > 0)
            SpawnWaveOfNPCs(0);
    }

    // Repositions active NPCs that are within <radius> of <center> and are not
    // currently visible to the player. Uses the same valid-spawn logic as RespawnAll.
    // Called by RoomNPCShuffle after the player has been inside a safe/spawn room
    // with all doors closed for long enough.
    public void ShuffleNearbyNPCs(Vector3 center, float radius)
    {
        // Snapshot candidates so we never modify activeNPCs while iterating it.
        List<GameObject> candidates = new List<GameObject>();
        foreach (GameObject npc in activeNPCs)
        {
            if (npc != null && Vector3.Distance(npc.transform.position, center) <= radius)
                candidates.Add(npc);
        }

        int moved = 0;
        foreach (GameObject npc in candidates)
        {
            if (npc == null) continue;

            // Extra safety: never teleport an NPC the player can currently see,
            // even if a door is open or the sight-line somehow passes through.
            if (IsVisibleToPlayer(npc.transform.position)) continue;

            Vector3 newPos = GetValidSpawnPosition();
            if (newPos == Vector3.zero) continue;

            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled)
            {
                // Warp is the correct NavMeshAgent teleport — it re-samples the
                // NavMesh at the destination and updates the agent's internal state.
                agent.Warp(newPos);
            }
            else
            {
                npc.transform.position = newPos;
            }

            // Deactivate the NPC so it doesn't immediately chase if it spawned
            // within detection range. It will re-activate when the player looks at it.
            npc.GetComponent<NPCMovement>()?.ResetActivation();

            moved++;
        }

        if (moved > 0)
        {
            Debug.Log($"[NPCSpawnManager] ShuffleNearbyNPCs: repositioned {moved} NPC(s) near {center}.");

            // After warping, check if ANY remaining NPC is still within its detection range
            // of the player. If none are, nobody will call UpdateHeartbeat() next frame, so the
            // audio will permanently stick at the last high pitch/volume — reset it explicitly.
            // (This mirrors what GameManager.Respawn() does before re-spawning all NPCs.)
            bool anyStillClose = false;
            foreach (GameObject npc in activeNPCs)
            {
                if (npc == null) continue;
                NPCMovement movement = npc.GetComponent<NPCMovement>();
                if (movement == null) continue;
                // Use the NPC's current position — warped NPCs are already at their new spots.
                if (Vector3.Distance(npc.transform.position, player.position) <= movement.detectionRange)
                {
                    anyStillClose = true;
                    break;
                }
            }

            if (!anyStillClose && HeartbeatManager.Instance != null)
                HeartbeatManager.Instance.ResetHeartbeat();
        }
    }

    /// <summary>
    /// Repositions a single NPC (the weeping angel) to a new valid spawn position.
    /// Called by NPCMovement when the player holds the flashlight on the angel long enough.
    /// Uses the same valid-spawn logic as RespawnAll / ShuffleNearbyNPCs.
    /// </summary>
    public void RespawnSingleNPC(GameObject npc)
    {
        if (npc == null) return;

        Vector3 newPos = GetValidSpawnPosition();
        if (newPos == Vector3.zero)
        {
            Debug.LogWarning("[NPCSpawnManager] RespawnSingleNPC: no valid spawn position found.");
            return;
        }

        NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
            agent.Warp(newPos);
        else
            npc.transform.position = newPos;

        // Deactivate so the angel doesn't immediately chase if it landed near the player.
        npc.GetComponent<NPCMovement>()?.ResetActivation();

        Debug.Log($"[NPCSpawnManager] RespawnSingleNPC: angel repositioned to {newPos}.");

        // Reset heartbeat if no NPC remains close enough to the player to drive it.
        bool anyStillClose = false;
        foreach (GameObject activeNpc in activeNPCs)
        {
            if (activeNpc == null) continue;
            NPCMovement movement = activeNpc.GetComponent<NPCMovement>();
            if (movement == null) continue;
            if (Vector3.Distance(activeNpc.transform.position, player.position) <= movement.detectionRange)
            {
                anyStillClose = true;
                break;
            }
        }

        if (!anyStillClose && HeartbeatManager.Instance != null)
            HeartbeatManager.Instance.ResetHeartbeat();
    }
}