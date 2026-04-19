using System;
using System.IO;
using UnityEngine;
using NavKeypad;

[Serializable]
public class SaveData
{
    public bool   hasData;
    public int    level;
    public string saveDate;
    public string saveTime;
    public int    slotIndex;

    // Player world position — restored after dungeon generation completes on load
    public float posX;
    public float posY;
    public float posZ;

    // Random seed used when this dungeon was generated.
    // Loading with the same seed reproduces the identical layout.
    public int dungeonSeed;

    // True once the player has completed the intro room sequence and entered the dungeon.
    // On load this bypasses the intro and activates gaze immediately.
    public bool introCompleted;

    // Collectibles the player picked up
    public bool gogglesCollected;
    public bool flashlightCollected;

    // Per-level state arrays (index = level number, padded to MAX_LEVELS)
    // Bitmask of which code digits (slots 0-3) were collected on each level.
    public int[] collectedDigitMask;
    // Whether the powerbox was activated on each outage level.
    public bool[] levelPowerRestored;
}

/// <summary>
/// Persists across scenes (DontDestroyOnLoad).
///
/// Saving  — call SaveCurrentGame() from anywhere (e.g. pause menu).
///
/// Loading — set the active slot then load the game scene.
///           SaveGameManager subscribes to GameManager.OnPlayerPlaced, which fires
///           after dungeon generation completes. It then:
///             1. Overrides the default spawn with the saved position.
///             2. Restores code collection, power state, collectibles.
///             3. Opens stairway doors for levels already descended through.
///             4. Activates correct level visibility (hides/shows floor parents).
///             5. Bypasses the intro room if already completed.
///
/// Determinism — the dungeon generator seeds Unity's RNG with a stored int so the
///               same seed always reproduces the identical dungeon layout.
/// </summary>
public class SaveGameManager : MonoBehaviour
{
    public static SaveGameManager Instance { get; private set; }

    public const int SLOT_COUNT = 3;
    public const int MAX_LEVELS = 5;

    private int        _activeSlot = -1;
    private SaveData[] _saves;
    private bool       _pendingLoad;
    private int        _pendingNewSeed;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _saves = new SaveData[SLOT_COUNT];
        LoadAllSlots();
    }

    private void OnEnable()  => GameManager.OnPlayerPlaced += OnPlayerPlaced;
    private void OnDisable() => GameManager.OnPlayerPlaced -= OnPlayerPlaced;

    // ─── Called once dungeon generation is complete ───────────────────────────

    private void OnPlayerPlaced()
    {
        if (!_pendingLoad) return;
        _pendingLoad = false;

        if (_activeSlot < 0 || _activeSlot >= SLOT_COUNT) return;
        var save = _saves[_activeSlot];
        if (save == null || !save.hasData) return;

        var gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogWarning("[SaveGameManager] GameManager not found — cannot restore save.");
            return;
        }

        // Restore power state BEFORE SetCurrentLevel so OnEnterLevel sees the correct
        // state and doesn't trigger FadeToDark / ShowPowerMessage on already-restored levels.
        if (PowerManager.Instance != null && save.levelPowerRestored != null)
        {
            for (int lvl = 0; lvl < save.levelPowerRestored.Length; lvl++)
            {
                if (save.levelPowerRestored[lvl])
                    PowerManager.Instance.RestorePowerStateQuiet(lvl);
            }
        }

        // Restore level (powers, code HUD, flashlight rates, etc.)
        gm.SetCurrentLevel(save.level);

        // Move player to saved position
        var savedPos = new Vector3(save.posX, save.posY, save.posZ);
        gm.TeleportPlayer(savedPos);

        // Anchor CameraControl's "original position" to the saved spawn point so that
        // ResetCameraState() on subsequent deaths returns the camera here, not back to
        // the intro room start (which is where it was anchored at generation time).
        gm.cameraControl?.UpdateOriginalTransform();

        // Restore everything the player did
        RestoreGameState(save);

        Debug.Log($"[SaveGameManager] Restored slot {_activeSlot} — Level {save.level + 1}, pos {savedPos}, seed {save.dungeonSeed}");
    }

    // ─── Game state restoration ───────────────────────────────────────────────

    private void RestoreGameState(SaveData save)
    {
        // 1. Code digits per level — marks pickups as collected, disables their GOs,
        //    updates the HUD, and locks/unlocks keypads appropriately.
        if (CodeNumberManager.Instance != null && save.collectedDigitMask != null)
        {
            for (int lvl = 0; lvl < save.collectedDigitMask.Length; lvl++)
            {
                int mask = save.collectedDigitMask[lvl];
                if (mask != 0)
                    CodeNumberManager.Instance.RestoreCollectedStateQuiet(lvl, mask);
            }
        }

        // 2. Power state — restores lights on outage levels and re-enables code numbers.
        if (PowerManager.Instance != null && save.levelPowerRestored != null)
        {
            for (int lvl = 0; lvl < save.levelPowerRestored.Length; lvl++)
            {
                if (save.levelPowerRestored[lvl])
                    PowerManager.Instance.RestorePowerStateQuiet(lvl);
            }
        }

        // 3. Collectibles — re-unlock controllers and remove pickups from the world.
        if (save.gogglesCollected)
        {
            GoggleController.Instance?.UnlockGoggles();
            DisablePickupsByComponentAndName<GazeItemPickup>("goggle");
            DisablePickupsByComponentAndName<ItemPickUp>("goggle");
        }
        if (save.flashlightCollected)
        {
            FlashlightController.Instance?.UnlockFlashlight();
            foreach (var p in FindObjectsOfType<FlashlightPickup>(true))
                if (p != null) p.gameObject.SetActive(false);
        }

        // 4. Stairway keypad doors — the player must have opened every door between
        //    level 0 and their current level, so re-open all of them.
        RestoreStairDoors(save.level);

        // 5. Level visibility — after InitialHide() only level 0 is active.
        //    Bring it in line with where the player actually saved.
        RestoreLevelVisibility(save.level);

        // 6. Intro room — if the player already completed the intro, activate gaze,
        //    restore dungeon HUD, and disable the intro room geometry.
        if (save.introCompleted)
            RestoreIntroState();

        // 7. Checkpoints — silently mark all checkpoints up to the player's current level
        //    as already activated so re-entering their rooms won't show "Checkpoint saved"
        //    again, and so the correct level's spawn point is set for respawn.
        RestoreCheckpoints(save.level);
    }

    // ── Stairway door restoration ─────────────────────────────────────────────

    public void RestoreStairDoors(int currentLevel)
    {
        if (currentLevel <= 0) return;

        // Each stairway that connects level N to N+1 has a SlidingDoor. Its parent
        // chain leads up to the "Stairways" persistent root. The stairway prefab root
        // sits directly under "Stairways" and is placed at Y = -(N+1) * levelHeight.
        float levelHeight = GetLevelHeight();

        foreach (var door in FindObjectsOfType<SlidingDoor>(true))
        {
            if (door == null) continue;

            Transform stairwayRoot = FindStairwayRoot(door.transform);
            if (stairwayRoot == null) continue;

            // stairwayRoot.y = -(levelIndex + 1) * levelHeight  →  levelIndex = round(-y/h) - 1
            int levelIndex = Mathf.RoundToInt(-stairwayRoot.position.y / levelHeight) - 1;

            if (levelIndex >= 0 && levelIndex < currentLevel)
            {
                door.OpenDoor();
                Debug.Log($"[SaveGameManager] Opened stair door for L{levelIndex}→L{levelIndex + 1}");
            }
        }
    }

    // Walks upward until we find the direct child of the "Stairways" root GO.
    // The stairway prefab's root is placed at the correct level Y position there.
    private static Transform FindStairwayRoot(Transform t)
    {
        while (t.parent != null)
        {
            if (t.parent.name == "Stairways") return t;
            t = t.parent;
        }
        return null; // not inside the Stairways hierarchy
    }

    // ── Level visibility restoration ──────────────────────────────────────────

    private void RestoreLevelVisibility(int currentLevel)
    {
        DungeonLevelVisibility vis = FindObjectOfType<DungeonLevelVisibility>();
        if (vis == null) return;

        // InitialHide() leaves only level 0 active. Correct that to match the saved level:
        // show the player's level and hide everything else.
        for (int i = 0; i < MAX_LEVELS; i++)
        {
            if (i == currentLevel)
                vis.ShowLevel(i);
            else
                vis.HideLevel(i);
        }

        Debug.Log($"[SaveGameManager] Restored level visibility — showing L{currentLevel} only.");
    }

    // ── Checkpoint restoration ────────────────────────────────────────────────

    private void RestoreCheckpoints(int currentLevel)
    {
        foreach (var cp in FindObjectsOfType<SpawnRoomCheckpoint>(true))
        {
            if (cp == null) continue;
            // Mark every checkpoint at or below the saved level as already activated.
            // This suppresses the "Checkpoint saved" notification when the player
            // walks back through a room they already visited before saving.
            if (cp.LevelIndex <= currentLevel)
                cp.MarkActivated();
        }
        Debug.Log($"[SaveGameManager] Pre-activated checkpoints for levels 0–{currentLevel}.");
    }

    // ── Intro room restoration ────────────────────────────────────────────────

    private void RestoreIntroState()
    {
        // Activate gaze tracking — the player already completed the face-scan step.
        GazeDetector gazeDetector = FindObjectOfType<GazeDetector>();
        gazeDetector?.SetGazeActive(true);

        // Show dungeon HUD elements that are hidden during the intro sequence.
        HUDManager.Instance?.SetIntroMode(false);

        // Disable the intro room geometry so it does not overlap the dungeon.
        IntroRoomSetup.Instance?.DisableIntroRoom();

        Debug.Log("[SaveGameManager] Restored intro state — gaze active, HUD in dungeon mode.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float GetLevelHeight()
    {
        DungeonLevelVisibility vis = FindObjectOfType<DungeonLevelVisibility>();
        return vis != null ? vis.LevelHeight : 4f;
    }

    private void DisablePickupsByComponentAndName<T>(string keyword) where T : Component
    {
        foreach (var comp in FindObjectsOfType<T>(true))
        {
            if (comp == null) continue;
            if (comp.gameObject.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                comp.gameObject.SetActive(false);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public SaveData GetSaveInfo(int slot)
    {
        if (slot < 0 || slot >= SLOT_COUNT) return new SaveData();
        return _saves[slot] ?? new SaveData { slotIndex = slot };
    }

    public int  GetActiveSlot()   => _activeSlot;
    public bool HasActiveSlot()   => _activeSlot >= 0;
    public bool HasPendingLoad()  => _pendingLoad;

    public SaveData GetCurrentSaveData()
    {
        if (_activeSlot < 0 || _activeSlot >= SLOT_COUNT) return null;
        return _saves[_activeSlot];
    }

    public void SetActiveSlot(int slot)
    {
        _activeSlot  = slot;
        _pendingLoad = true;
    }

    public void SetNewGameSlot(int slot)
    {
        _activeSlot  = slot;
        _pendingLoad = false;
    }

    public void ClearActiveSlot()
    {
        _activeSlot  = -1;
        _pendingLoad = false;
    }

    public void RecordDungeonSeed(int seed)
    {
        _pendingNewSeed = seed;
        if (_activeSlot >= 0 && _activeSlot < SLOT_COUNT && _saves[_activeSlot] != null)
            _saves[_activeSlot].dungeonSeed = seed;
    }

    public void SaveCurrentGame(int slotOverride = -1)
    {
        int slot = slotOverride >= 0 ? slotOverride : _activeSlot;
        if (slot < 0) slot = 0;
        _activeSlot = slot;

        var gm  = GameManager.Instance;
        int lvl = gm != null ? gm.GetCurrentLevel() : 0;
        var pos = (gm != null && gm.player != null) ? gm.player.position : Vector3.zero;

        int[] digitMasks = new int[MAX_LEVELS];
        if (CodeNumberManager.Instance != null)
            for (int i = 0; i < MAX_LEVELS; i++)
                digitMasks[i] = CodeNumberManager.Instance.GetCollectedMask(i);

        bool[] powerRestored = new bool[MAX_LEVELS];
        if (PowerManager.Instance != null)
            for (int i = 0; i < MAX_LEVELS; i++)
                powerRestored[i] = PowerManager.Instance.GetPowerRestored(i);

        int seed = 0;
        if (_saves[slot] != null && _saves[slot].hasData)
            seed = _saves[slot].dungeonSeed;
        if (seed == 0) seed = _pendingNewSeed;

        var data = new SaveData
        {
            hasData             = true,
            level               = lvl,
            saveDate            = DateTime.Now.ToString("MMM dd, yyyy"),
            saveTime            = DateTime.Now.ToString("HH:mm"),
            slotIndex           = slot,
            posX                = pos.x,
            posY                = pos.y,
            posZ                = pos.z,
            dungeonSeed         = seed,
            // The player can only reach the pause menu from inside the dungeon,
            // so the intro is always completed by the time they can save.
            introCompleted      = true,
            gogglesCollected    = GoggleController.Instance != null && GoggleController.Instance.GogglesUnlocked,
            flashlightCollected = FlashlightController.Instance != null && FlashlightController.Instance.IsUnlocked,
            collectedDigitMask  = digitMasks,
            levelPowerRestored  = powerRestored,
        };

        _saves[slot] = data;
        WriteToDisk(slot, data);
        Debug.Log($"[SaveGameManager] Saved slot {slot} — Level {lvl + 1}, seed {seed}");
    }

    public void DeleteSlot(int slot)
    {
        if (slot < 0 || slot >= SLOT_COUNT) return;
        _saves[slot] = new SaveData { slotIndex = slot };
        string path = GetPath(slot);
        if (File.Exists(path)) File.Delete(path);
        if (_activeSlot == slot) ClearActiveSlot();
        Debug.Log($"[SaveGameManager] Deleted slot {slot}");
    }

    // ─── Disk I/O ─────────────────────────────────────────────────────────────

    private void LoadAllSlots()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            string path = GetPath(i);
            if (File.Exists(path))
            {
                try   { _saves[i] = JsonUtility.FromJson<SaveData>(File.ReadAllText(path)); }
                catch { _saves[i] = new SaveData { slotIndex = i }; }
            }
            else
            {
                _saves[i] = new SaveData { slotIndex = i };
            }
        }
    }

    private void WriteToDisk(int slot, SaveData data)
    {
        try   { File.WriteAllText(GetPath(slot), JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogError($"[SaveGameManager] Write failed: {e.Message}"); }
    }

    private static string GetPath(int slot)
        => Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.json");
}
