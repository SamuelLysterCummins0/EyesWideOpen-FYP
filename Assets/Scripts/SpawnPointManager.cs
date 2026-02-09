using UnityEngine;
using System.Collections.Generic;

public class SpawnPointManager : MonoBehaviour
{
    public Transform[] spawnPoints;
    public Transform cutsceneParent;
    public Transform[] itemSpawnPositions;
    public GameObject itemPrefab;
    internal int currentSpawnIndex = -1;
    internal List<Vector3> usedSpawnPoints = new List<Vector3>();

    public int GetCurrentSpawnIndex() => currentSpawnIndex;
    public List<Vector3> GetUsedSpawnPoints() => usedSpawnPoints;

    void Start()
    {
        int randomIndex = Random.Range(0, spawnPoints.Length);
        
        Transform chosenSpawnPoint = spawnPoints[randomIndex];

        cutsceneParent.position = chosenSpawnPoint.position;
        
        if (randomIndex < itemSpawnPositions.Length && itemSpawnPositions[randomIndex] != null)
        {
            Transform itemSpawnPosition = itemSpawnPositions[randomIndex];
            
            if (itemPrefab != null)
            {
                GameObject itemInstance = Instantiate(itemPrefab, itemSpawnPosition.position, itemSpawnPosition.rotation);
                itemInstance.SetActive(true);
            }
            else
            {
                Debug.LogWarning("Item prefab is not assigned in the SpawnPointManager script.");
            }
        }
    }

    public void RestoreSpawnPoints(int spawnIndex, List<Vector3> savedSpawnPoints)
    {
        currentSpawnIndex = spawnIndex;
        usedSpawnPoints = savedSpawnPoints;
    }
}