using UnityEngine;
using System.Collections.Generic;

public class RandomSpawner : MonoBehaviour
{
    public GameObject applePrefab;
    public GameObject npcPrefab;
    public Transform[] spawnPoints;

    internal List<GameObject> spawnedObjects = new List<GameObject>();

    public List<GameObject> GetSpawnedObjects() => spawnedObjects;

    private void Start()
    {
        SpawnRandomObjects();
    }

    private void SpawnRandomObjects()
    {
        foreach (Transform spawnPoint in spawnPoints)
        {
            int randomObject = Random.Range(0, 3);
            if (randomObject == 0)
            {
                GameObject apple = Instantiate(applePrefab, spawnPoint.position, Quaternion.identity);
            }
            else if (randomObject == 1)
            {
                GameObject npc = Instantiate(npcPrefab, spawnPoint.position, Quaternion.identity);
                NPCMovement npcMovement = npc.GetComponent<NPCMovement>();
                npcMovement.player = GameObject.FindGameObjectWithTag("Player").transform;
                npcMovement.gameManager = FindObjectOfType<GameManager>();
                npcMovement.cameraControl = FindObjectOfType<CameraControl>();
                npcMovement.jumpscareAudio = FindObjectOfType<AudioSource>();
            }
        }
    }

     public void ClearSpawns()
    {
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
                Destroy(obj);
        }
        spawnedObjects.Clear();
    }

    public void SpawnObjectAtPosition(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject obj = Instantiate(prefab, position, rotation);
        spawnedObjects.Add(obj);
    }
}