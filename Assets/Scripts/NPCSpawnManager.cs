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
    public LayerMask obstaclesLayer;

    // Spawn zone settings
    public List<Transform> spawnZones = new List<Transform>();
    public float minDistanceFromPlayer = 15f;
    public float minDistanceBetweenNPCs = 10f;
    public bool showSpawnZones = true;

    private List<GameObject> activeNPCs = new List<GameObject>();
    private List<GameObject> activeApples = new List<GameObject>();
    private Camera playerCamera;

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
        {
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
        }

        Debug.Log($"{name}: NPCSpawnManager started with {spawnZones.Count} spawn zones");

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
            GameObject npc = Instantiate(npcPrefab, spawnPosition, Quaternion.identity);
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
            activeApples.Add(apple);
        }
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

        npcMovement.obstaclesLayer = obstaclesLayer;

        Debug.Log($"NPC Setup Complete - ObstaclesLayer: {npcMovement.obstaclesLayer.value}");
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
            && IsFarEnoughFromOtherNPCs(position);
    }

    private bool IsVisibleToPlayer(Vector3 position)
    {
        if (playerCamera == null) return false;

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
}
