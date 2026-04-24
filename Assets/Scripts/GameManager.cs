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
    [Tooltip("WinScreen component on the Victory Canvas. Handles fade-to-black and 'You Escaped' text.")]
    public WinScreen winScreen;

    [Header("State")]
    private int currentLevel = 0;

    // Fired after the dungeon finishes generating and the player is placed.
    // SaveGameManager subscribes to override position/level when loading a save.
    public static System.Action OnPlayerPlaced;
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

        if (ambientClip != null)
        {
            ambientSource = gameObject.AddComponent<AudioSource>();
            ambientSource.clip        = ambientClip;
            ambientSource.loop        = true;
            ambientSource.volume      = ambientVolume;
            ambientSource.spatialBlend = 0f;
            ambientSource.playOnAwake = false;
            ambientSource.Play();
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (playerController == null && player != null)
            playerController = player.GetComponent<SUPERCharacterAIO>();

        // Freeze the player while the dungeon generates — TeleportPlayer() will unfreeze them.
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

    public void SetDetonationRespawnPoint(Vector3 pos)
    {
        detonationRespawnPoint  = pos;
        detonationRespawnActive = true;
        detonationRespawnLevel  = currentLevel;
    }

    public void ClearDetonationRespawn()
    {
        detonationRespawnActive = false;
    }

    public void TriggerVictory()
    {
        DetonationManager.Instance?.OnVictory();
        ClearDetonationRespawn();

        if (playerController != null)
            playerController.enabled = false;

        if (winScreen != null)
            winScreen.ShowVictory();
        else
        {
            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    public void PlacePlayerAtSpawnRoom()
    {
        IntroRoomSetup introRoom = FindObjectOfType<IntroRoomSetup>();
        if (introRoom != null && introRoom.HasPlayerStart())
        {
            TeleportPlayer(introRoom.GetPlayerStartPosition());

            if (cameraControl != null)
                cameraControl.UpdateOriginalTransform();

            HUDManager.Instance?.SetIntroMode(true);
            OnPlayerPlaced?.Invoke();
            return;
        }

        if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(0))
        {
            Debug.LogWarning("GameManager: Spawn room not ready yet for level 0.");
            return;
        }

        TeleportPlayer(spawnRoomSetup.GetSpawnPoint(0));

        if (cameraControl != null)
            cameraControl.UpdateOriginalTransform();

        OnPlayerPlaced?.Invoke();
    }

    public void SkipToSpawnRoom()
    {
        GazeDetector gazeDetector = FindObjectOfType<GazeDetector>();
        if (gazeDetector != null)
            gazeDetector.SetGazeActive(true);
        else
            Debug.LogWarning("[SkipToSpawnRoom] GazeDetector not found in scene.");

        if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(0))
        {
            Debug.LogWarning("[SkipToSpawnRoom] Level 0 spawn point not ready — dungeon may still be generating.");
            return;
        }

        TeleportPlayer(spawnRoomSetup.GetSpawnPoint(0));

        if (cameraControl != null)
            cameraControl.UpdateOriginalTransform();

        SetCurrentLevel(0);
        HUDManager.Instance?.SetIntroMode(false);
        IntroRoomSetup.Instance?.DisableIntroRoom();

        Debug.Log("[DebugTools] Skipped intro room — player at Level 0 spawn room, gaze active.");
    }

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

    public void Respawn()
    {
        if (deathCoroutine != null)
        {
            StopCoroutine(deathCoroutine);
            deathCoroutine = null;
        }

        isDeathScreenShowing = false;
        isPlayerDead = false;
        Time.timeScale = 1f;

        if (cameraControl != null)
        {
            try { cameraControl.ResetCameraState(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameManager] cameraControl.ResetCameraState() threw an exception — camera state may be incorrect, but respawn will continue. Exception: {e}");
            }
        }

        Vector3 spawnPoint = Vector3.zero;

        if (detonationRespawnActive)
        {
            spawnPoint = detonationRespawnPoint;
            currentLevel = detonationRespawnLevel;
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
            TeleportPlayer(spawnPoint);

            if (cameraControl != null)
                cameraControl.UpdateOriginalTransform();

            DetonationManager.Instance?.OnRespawn();

            if (playerController != null)
                playerController.currentStaminaLevel = playerController.Stamina;

            InsanityManager.Instance?.SetInsanity(0f);
            PlayerSafeZone.Reset();
            FlashlightController.Instance?.RefillBattery();
            SirenPhaseManager.Instance?.CancelSiren();

            if (HeartbeatManager.Instance != null)
                HeartbeatManager.Instance.ResetHeartbeat();

            try
            {
                if (dungeonNavMeshSetup != null)
                    dungeonNavMeshSetup.RespawnNPCsForLevel(currentLevel);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameManager] RespawnNPCsForLevel threw: {e}");
            }

            foreach (PacerNPC pacer in FindObjectsOfType<PacerNPC>())
                pacer.ResetForRespawn();

            if (CodeNumberManager.Instance != null)
                CodeNumberManager.Instance.ResetCollectionForLevel(currentLevel);
            else
                Debug.LogError("[GameManager] CodeNumberManager.Instance is NULL during Respawn.");

            if (playerController != null)
                playerController.enabled = true;

            ComputerInteraction ci = computerRoomSetup?.GetComputerInteractionForLevel(currentLevel);
            ci?.ResetForRespawn();
        }
        finally
        {
            deathScreen.SetActive(false);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    // Safely teleports the player: zeros physics, moves, re-enables movement.
    public void TeleportPlayer(Vector3 destination)
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

    // -1 = no regen pending
    private int _regenTargetLevel = -1;

    public void RegenerateDungeon()
    {
        ProceduralDungeonGenerator gen = FindObjectOfType<ProceduralDungeonGenerator>();
        if (gen == null)
        {
            Debug.LogError("[GameManager] RegenerateDungeon: ProceduralDungeonGenerator not found in scene — aborting.");
            return;
        }

        _regenTargetLevel = currentLevel;
        OnPlayerPlaced += OnRegenPlayerPlaced;

        gen.ClearDungeon();
        gen.GenerateDungeon();

        FlashlightController.Instance?.RefillBattery();
        SirenPhaseManager.Instance?.CancelSiren();
    }

    private void OnRegenPlayerPlaced()
    {
        OnPlayerPlaced -= OnRegenPlayerPlaced;

        int target = _regenTargetLevel;
        _regenTargetLevel = -1;

        if (target <= 0)
            return;

        IntroRoomSetup.Instance?.DisableIntroRoom();
        HUDManager.Instance?.SetIntroMode(false);

        GazeDetector gaze = FindObjectOfType<GazeDetector>();
        if (gaze != null)
            gaze.SetGazeActive(true);

        // Sync level visibility before teleporting — hidden levels have no geometry active.
        DungeonLevelVisibility vis = FindObjectOfType<DungeonLevelVisibility>();
        if (vis != null)
            vis.ShowOnlyLevel(target);
        else
            Debug.LogWarning("[GameManager] RegenerateDungeon: DungeonLevelVisibility not found — target level may be hidden.");

        SaveGameManager.Instance?.RestoreStairDoors(target);

        if (spawnRoomSetup != null && spawnRoomSetup.HasSpawnPoint(target))
        {
            TeleportPlayer(spawnRoomSetup.GetSpawnPoint(target));
            SetCurrentLevel(target);

            if (cameraControl != null)
                cameraControl.UpdateOriginalTransform();
        }
        else
        {
            Debug.LogWarning($"[GameManager] RegenerateDungeon: no spawn point for level {target} after regen.");
        }
    }

    public void ResetCurrentLevel()
    {
        Respawn();
    }
}