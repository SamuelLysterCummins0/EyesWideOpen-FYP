using UnityEngine;

public class FlareController : MonoBehaviour
{
    public static FlareController Instance { get; private set; }

    [Header("Flare Settings")]
    public GameObject flarePrefab;
    public float flareDuration = 5f;
    public float monsterAttractionRadius = 10f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void UseFlare()
    {
        if (flarePrefab == null)
        {
            Debug.LogError("Flare prefab is not assigned!");
            return;
        }

        Vector3 playerPos = Camera.main.transform.parent.position;
        GameObject flare = null;
        
        try
        {
            flare = Instantiate(flarePrefab, playerPos, Quaternion.identity);
            
            ParticleSystem smoke = flare.GetComponentInChildren<ParticleSystem>();
            if (smoke != null)
            {
                smoke.Stop();
                smoke.Clear();
                smoke.Play();
            }

            // Start attracting monsters
            StartCoroutine(AttractMonsters(flare));

            // Remove from inventory
            if (HotbarManager.Instance != null)
            {
                HotbarManager.Instance.RemoveActiveItem();
            }

            // Destroy after duration
            Destroy(flare, flareDuration);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error spawning flare: {e.Message}");
        }
    }

    private System.Collections.IEnumerator AttractMonsters(GameObject flare)
    {
        float elapsedTime = 0;

        while (elapsedTime < flareDuration && flare != null)
        {
            // Find all monsters
            NPCMovement[] monsters = FindObjectsOfType<NPCMovement>();
            
            foreach (var monster in monsters)
            {
                // Only attract monsters that aren't being looked at
                if (monster != null && monster.enabled)
                {
                    monster.SetFlareTarget(flare.transform);
                }
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        NPCMovement[] remainingMonsters = FindObjectsOfType<NPCMovement>();
        foreach (var monster in remainingMonsters)
        {
            if (monster != null)
            {
                monster.ClearFlareTarget();
            }
        }
    }
}