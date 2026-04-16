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
    public ComputerRoomSetup computerRoomSetup;
    public CameraControl cameraControl;

    [Header("Ambient Audio")]
    [Tooltip("Looping ambient clip that plays constantly across all levels (e.g. electrical hum / static).")]
    [SerializeField] private AudioClip ambientClip;
    [SerializeField] [Range(0f, 1f)] private float ambientVolume = 0.35f;
    private AudioSource ambientSource;

    [Header("Victory")]
    [Tooltip("UI panel shown when the player escapes before the detonation timer runs out. " +
             "Design it similarly to deathScreen — a full-screen overlay with an escape message.")]
    public GameObject victoryScreen;

    [Header("State")]
    private int currentLevel = 0;
    private bool isDeathScreenShowing = false;
    private bool isPlayerDead = false;
    private Coroutine deathCoroutine;

    // Detonation sequence respawn override — when set, Respawn() sends the player
    // back to the detonation room entrance instead of the current level's spawn room.
    private bool    detonationRespawnActive = false;
    private Vector3 detonationRespawnPoint  = Vector3.zero;
    private int     detonationRespawnLevel  = 4; // level to restore when respawning mid-escape


    void Start()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        if (respawnButton != null)
            respawnButton.onClick.AddListener(Respawn);
        else
            Debug.LogWarning("[GameManager] respawnButton is not assigned in the Inspector — respawn will only work if DeathScreen also has it assigned.");

        // Start the ambient loop immediately — plays for the entire session.
        if (ambientClip != null)
        {
            ambientSource = gameObject.AddComponent<AudioSource>();
            ambientSource.clip        = ambientClip;
            ambientSource.loop        = true;
            ambientSource.volume      = ambientVolume;
            ambientSource.spatialBlend = 0f; // 2D — heard equally everywhere
            ambientSource.playOnAwake = false;
            ambientSource.Play();
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (playerController == null && player != null)
            playerController = player.GetComponent<SUPERCharacterAIO>();

        // Debug: log camera state before we disable the controller so we can see
        // if something is already wrong with the camera parent at startup.
        if (Camera.main != null)
            Debug.Log($"[GameManager] Camera parent at Start: '{Camera.main.transform.parent?.name ?? "NULL (unparented)"}', pos={Camera.main.transform.position}");

        // Freeze the player while the dungeon generates so they don't fall through
        // geometry or drift away from (0,0,0) before PlacePlayerAtSpawnRoom() fires.
        // TeleportPlayer() (called by PlacePlayerAtSpawnRoom) will unfreeze them.
        if (playerController != null)
            playerController.enabled = false;
        if (player != null)
        {
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity        = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic     = true;
            }
        }

        // Auto-find references if not assigned in Inspector
        if (spawnRoomSetup == null)
            spawnRoomSetup = FindObjectOfType<SpawnRoomSetup>();
        if (dungeonNavMeshSetup == null)
            dungeonNavMeshSetup = FindObjectOfType<DungeonNavMeshSetup>();
        if (computerRoomSetup == null)
            computerRoomSetup = FindObjectOfType<ComputerRoomSetup>();

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

        // Notify new systems of level change
        InsanityManager.Instance?.OnLevelChanged(level);
        SirenPhaseManager.Instance?.OnLevelChanged(level);
        FlashlightController.Instance?.SetLevel(level);
    }

    public int GetCurrentLevel() => currentLevel;

    // ──────────────────────────────────────────────
    // DETONATION RESPAWN OVERRIDE
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called by DetonationManager when the sequence starts.
    /// Overrides the respawn point so death/timeout always returns the player
    /// to the detonation room entrance on the last level.
    /// </summary>
    public void SetDetonationRespawnPoint(Vector3 pos)
    {
        detonationRespawnPoint  = pos;
        detonationRespawnActive = true;
        detonationRespawnLevel  = currentLevel; // capture the last level index at sequence start
        Debug.Log($"[GameManager] Detonation respawn point set to {pos}");
    }

    /// <summary>Called by DetonationManager.OnRespawn() and OnVictory() to restore normal respawn.</summary>
    public void ClearDetonationRespawn()
    {
        detonationRespawnActive = false;
        Debug.Log("[GameManager] Detonation respawn override cleared.");
    }

    // ──────────────────────────────────────────────
    // VICTORY
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called by WinTrigger when the player reaches level 0's spawn room during detonation.
    /// Stops the detonation sequence and shows the victory screen.
    /// </summary>
    public void TriggerVictory()
    {
        Debug.Log("[GameManager] TriggerVictory() called — player escaped!");

        // Let DetonationManager clean up lighting / UI first
        DetonationManager.Instance?.OnVictory();
        ClearDetonationRespawn();

        if (victoryScreen != null)
            victoryScreen.SetActive(true);

        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // Called by ProceduralDungeonGenerator at the end of GenerateDungeonCoroutine
    // to move the player to their starting position on first load.
    // If an intro room is present the player starts there; otherwise falls back
    // to the dungeon's level 0 spawn room.
    public void PlacePlayerAtSpawnRoom()
    {
        // Prefer the intro room's PlayerStart so the player begins the game
        // inside the security entry suite, not directly in the dungeon.
        IntroRoomSetup introRoom = FindObjectOfType<IntroRoomSetup>();
        if (introRoom != null && introRoom.HasPlayerStart())
        {
            Vector3 introStart = introRoom.GetPlayerStartPosition();
            Debug.Log($"[GameManager] PlacePlayerAtSpawnRoom → intro room PlayerStart at {introStart}");
            TeleportPlayer(introStart);

            if (cameraControl != null)
                cameraControl.UpdateOriginalTransform();
            return;
        }

        // Fallback: no intro room — teleport straight to the dungeon spawn room.
        if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(0))
        {
            Debug.LogWarning("GameManager: Spawn room not ready yet for level 0.");
            return;
        }

        Vector3 spawnPos = spawnRoomSetup.GetSpawnPoint(0);
        Debug.Log($"[GameManager] PlacePlayerAtSpawnRoom → dungeon spawn room at {spawnPos}");
        TeleportPlayer(spawnPos);

        if (cameraControl != null)
            cameraControl.UpdateOriginalTransform();
    }

    // ──────────────────────────────────────────────
    // DEBUG — SKIP INTRO ROOM
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called by DebugTools (F5) to bypass the entire intro-room sequence.
    /// Activates gaze tracking, teleports the player directly to the Level 0
    /// dungeon spawn room, and restores all dungeon HUD elements.
    /// </summary>
    public void SkipToSpawnRoom()
    {
        // 1. Activate gaze — simulates completing the face-scan / calibration step
        GazeDetector gazeDetector = FindObjectOfType<GazeDetector>();
        if (gazeDetector != null)
            gazeDetector.SetGazeActive(true);
        else
            Debug.LogWarning("[SkipToSpawnRoom] GazeDetector not found in scene.");

        // 2. Teleport to the Level 0 dungeon spawn room
        if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(0))
        {
            Debug.LogWarning("[SkipToSpawnRoom] Level 0 spawn point not ready — dungeon may still be generating.");
            return;
        }

        TeleportPlayer(spawnRoomSetup.GetSpawnPoint(0));

        if (cameraControl != null)
            cameraControl.UpdateOriginalTransform();

        // 3. Initialise dungeon state for level 0 (power, code HUD, flashlight rates, etc.)
        SetCurrentLevel(0);

        // 4. Restore HUD elements that are hidden while the intro room is active
        HUDManager.Instance?.SetIntroMode(false);

        // 5. Disable intro room geometry to avoid visual overlap and save performance
        IntroRoomSetup.Instance?.DisableIntroRoom();

        Debug.Log("[DebugTools] Skipped intro room — player at Level 0 spawn room, gaze active.");
    }

    // ──────────────────────────────────────────────
    // DEATH
    // ──────────────────────────────────────────────

    public void PlayerDied()
    {
        Debug.Log("[GameManager] PlayerDied() called.");
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
        Debug.Log("[GameManager] Respawn() called.");

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

        if (detonationRespawnActive)
        {
            // Detonation sequence is active — always send the player back to the
            // detonation room entrance regardless of which level they died on.
            spawnPoint = detonationRespawnPoint;
            currentLevel = detonationRespawnLevel; // restore level so managers stay in sync
            Debug.Log($"[GameManager] Detonation respawn override active — teleporting to {spawnPoint}");
        }
        else if (spawnRoomSetup != null && spawnRoomSetup.HasSpawnPoint(currentLevel))
        {
            spawnPoint = spawnRoomSetup.GetSpawnPoint(currentLevel);
        }
        else
        {
            Debug.LogWarning($"GameManager: No spawn point for level {currentLevel}, respawning at origin.");
        }

        try
        {
            Debug.Log("[GameManager] Respawn try-block: TeleportPlayer");
            TeleportPlayer(spawnPoint);

            // Update CameraControl's stored original position AFTER teleporting so
            // a subsequent ResetCameraState() targets the spawn point, not world-origin.
            Debug.Log("[GameManager] Respawn try-block: UpdateOriginalTransform");
            if (cameraControl != null)
                cameraControl.UpdateOriginalTransform();

            // If a detonation sequence was running, reset it now (stop timer, restore
            // lighting, clear respawn override) so the player must press the button again.
            Debug.Log("[GameManager] Respawn try-block: DetonationManager.OnRespawn");
            DetonationManager.Instance?.OnRespawn();

            // Reset stamina to full
            Debug.Log("[GameManager] Respawn try-block: stamina reset");
            if (playerController != null)
                playerController.currentStaminaLevel = playerController.Stamina;

            // Reset heartbeat BEFORE NPC respawn so it can't be overridden by a
            // dying NPC's final Update() or a freshly spawned NPC's first Update()
            Debug.Log("[GameManager] Respawn try-block: HeartbeatManager");
            if (HeartbeatManager.Instance != null)
                HeartbeatManager.Instance.ResetHeartbeat();

            // Re-randomise NPC positions for this level.
            // Wrapped in its own try-catch so any NPC-respawn failure cannot skip
            // the code-number reset that follows (they are independent operations).
            Debug.Log("[GameManager] Respawn try-block: RespawnNPCsForLevel");
            try
            {
                if (dungeonNavMeshSetup != null)
                    dungeonNavMeshSetup.RespawnNPCsForLevel(currentLevel);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameManager] RespawnNPCsForLevel threw an exception — NPCs may not have respawned correctly, but the rest of the respawn will proceed. Exception: {e}");
            }

            // Reset the Pacer NPC so it can chase again after the player respawns.
            FindObjectOfType<PacerNPC>()?.ResetForRespawn();

            // Reset collected code numbers so the player must gather them again.
            // Wall number positions, digit values, and level layout are unchanged.
            Debug.Log("[GameManager] Respawn try-block: CodeNumberManager.ResetCollectionForLevel");
            if (CodeNumberManager.Instance != null)
                CodeNumberManager.Instance.ResetCollectionForLevel(currentLevel);
            else
                Debug.LogError("[GameManager] CodeNumberManager.Instance is NULL during Respawn — numbers will not reset! Check that the CodeNumberManager GameObject is active in the scene and hasn't been destroyed.");

            // Re-enable the player controller (it was disabled via PlayerDied).
            // TeleportPlayer already did this above, but guard here in case of edge cases.
            if (playerController != null)
                playerController.enabled = true;

            // Unlock the computer terminal for this level so digit 3 can be re-earned.
            ComputerInteraction ci = computerRoomSetup?.GetComputerInteractionForLevel(currentLevel);
            ci?.ResetForRespawn();
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