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
    public CameraControl cameraControl;

    [Header("State")]
    private int currentLevel = 0;
    private bool isDeathScreenShowing = false;
    private bool isPlayerDead = false;
    private Coroutine deathCoroutine;

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

    // Call this whenever the player descends or ascends to a new level.
    // Also resets the code number HUD so the new level's digits display correctly.
    public void SetCurrentLevel(int level)
    {
        currentLevel = level;
        Debug.Log($"[GameManager] SetCurrentLevel({level})");

        if (PowerManager.Instance == null)
            Debug.LogError("[GameManager] PowerManager.Instance is NULL — add a PowerManager component to a GameObject in the scene!");
        else
            PowerManager.Instance.OnEnterLevel(level);

        bool powerOn = PowerManager.Instance == null || PowerManager.Instance.IsPowerOn;
        if (powerOn && CodeNumberManager.Instance != null)
            CodeNumberManager.Instance.ActivateLevel(level);
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
        if (isPlayerDead) return;
        isPlayerDead = true;

        // Disable controller immediately so the player doesn't drift during the jumpscare
        if (playerController != null)
            playerController.enabled = false;

        deathCoroutine = StartCoroutine(HandlePlayerDeath());
    }

    private System.Collections.IEnumerator HandlePlayerDeath()
    {
        // Wait for jumpscare animation to finish — driven by CameraControl.jumpscareHoldTime
        float waitTime = (cameraControl != null) ? cameraControl.jumpscareHoldTime : 3f;
        yield return new WaitForSeconds(waitTime);

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
        // Stop the death coroutine in case it's still pending
        if (deathCoroutine != null)
        {
            StopCoroutine(deathCoroutine);
            deathCoroutine = null;
        }

        // Stop any lingering camera coroutine (ReturnCameraToOriginal gets stuck
        // when timeScale=0 and would otherwise resume mid-respawn)
        if (cameraControl != null)
            cameraControl.ResetCameraState();

        isDeathScreenShowing = false;
        isPlayerDead = false;
        Time.timeScale = 1f;

        // Do all world-state changes while the death screen still covers the view,
        // so the player never sees themselves warp across the map.
        Vector3 spawnPoint = Vector3.zero;
        if (spawnRoomSetup != null && spawnRoomSetup.HasSpawnPoint(currentLevel))
            spawnPoint = spawnRoomSetup.GetSpawnPoint(currentLevel);
        else
            Debug.LogWarning($"GameManager: No spawn point for level {currentLevel}, respawning at origin.");

        try
        {
            TeleportPlayer(spawnPoint);

            // Reset stamina to full
            if (playerController != null)
                playerController.currentStaminaLevel = playerController.Stamina;

            // Reset heartbeat BEFORE NPC respawn so it can't be overridden by a
            // dying NPC's final Update() or a freshly spawned NPC's first Update()
            if (HeartbeatManager.Instance != null)
                HeartbeatManager.Instance.ResetHeartbeat();

            // Re-randomise NPC positions for this level
            if (dungeonNavMeshSetup != null)
                dungeonNavMeshSetup.RespawnNPCsForLevel(currentLevel);
        }
        finally
        {
            // Hide death screen — inside finally so it runs even if something above throws
            deathScreen.SetActive(false);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
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