using UnityEngine;
using UnityEngine.UI;
using SUPERCharacter;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("References")]
    public Transform player;
    public GameObject deathScreen;
    public Button respawnButton;
    public SUPERCharacterAIO playerController;
    public SpawnRoomSetup spawnRoomSetup;
    public DungeonNavMeshSetup dungeonNavMeshSetup;

    [Header("State")]
    private int currentLevel = 0;
    private bool isDeathScreenShowing = false;

    void Start()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        respawnButton.onClick.AddListener(Respawn);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (playerController == null && player != null)
            playerController = player.GetComponent<SUPERCharacterAIO>();

        // Auto-find references if not assigned in Inspector
        if (spawnRoomSetup == null)
            spawnRoomSetup = FindObjectOfType<SpawnRoomSetup>();
        if (dungeonNavMeshSetup == null)
            dungeonNavMeshSetup = FindObjectOfType<DungeonNavMeshSetup>();
    }

    // Call this whenever the player descends or ascends to a new level
    public void SetCurrentLevel(int level)
    {
        currentLevel = level;
    }

    public int GetCurrentLevel() => currentLevel;

    // Called by ProceduralDungeonGenerator at the end of GenerateDungeonCoroutine
    // to move the player into the level 0 spawn room on first load.
    public void PlacePlayerAtSpawnRoom()
    {
        if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(0))
        {
            Debug.LogWarning("GameManager: Spawn room not ready yet for level 0.");
            return;
        }

        TeleportPlayer(spawnRoomSetup.GetSpawnPoint(0));
    }

    // ──────────────────────────────────────────────
    // DEATH
    // ──────────────────────────────────────────────

    public void PlayerDied()
    {
        // Disable controller immediately so the player doesn't drift during the jumpscare
        if (playerController != null)
            playerController.enabled = false;

        StartCoroutine(HandlePlayerDeath());
    }

    private System.Collections.IEnumerator HandlePlayerDeath()
    {
        // Wait for jumpscare animation to finish
        yield return new WaitForSeconds(5f);

        // Show death screen
        deathScreen.SetActive(true);
        isDeathScreenShowing = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        Time.timeScale = 0f;
    }

    // ──────────────────────────────────────────────
    // RESPAWN
    // ──────────────────────────────────────────────

    public void Respawn()
    {
        // Hide death screen, restore time and cursor
        deathScreen.SetActive(false);
        isDeathScreenShowing = false;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        Time.timeScale = 1f;

        // Teleport player to this level's spawn room
        Vector3 spawnPoint = Vector3.zero;
        if (spawnRoomSetup != null && spawnRoomSetup.HasSpawnPoint(currentLevel))
            spawnPoint = spawnRoomSetup.GetSpawnPoint(currentLevel);
        else
            Debug.LogWarning($"GameManager: No spawn point for level {currentLevel}, respawning at origin.");

        TeleportPlayer(spawnPoint);

        // Re-randomise NPC positions for this level
        if (dungeonNavMeshSetup != null)
            dungeonNavMeshSetup.RespawnNPCsForLevel(currentLevel);

        // Reset audio feedback
        if (HeartbeatManager.Instance != null)
            HeartbeatManager.Instance.ResetHeartbeat();
    }

    // Safely teleports the player: zeros physics, moves, re-enables movement.
    private void TeleportPlayer(Vector3 destination)
    {
        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        player.position = destination;

        if (rb != null)
            rb.isKinematic = false;

        if (playerController != null)
            playerController.enabled = true;
    }

    public bool IsDeathScreenActive() => isDeathScreenShowing;
}