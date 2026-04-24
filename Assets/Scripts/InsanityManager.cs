using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owns the player's insanity value (0-100). Other systems push increases in via
// AddInsanity and subscribe to the events below. Decay happens automatically
// based on which room the player is standing in.
public class InsanityManager : MonoBehaviour
{
    public static InsanityManager Instance { get; private set; }

    [Header("Insanity Rates — Decay (per second)")]
    [Tooltip("Passive decay in open dungeon while Presence is inactive.")]
    [SerializeField] private float naturalDecayRate   = 0.5f;
    [Tooltip("Decay rate inside safe rooms.")]
    [SerializeField] private float safeRoomDecayRate  = 5f;
    [Tooltip("Decay rate inside spawn rooms.")]
    [SerializeField] private float spawnRoomDecayRate = 4f;
    [Tooltip("Decay rate inside hidden rooms.")]
    [SerializeField] private float hiddenRoomDecayRate = 3f;
    [Tooltip("Decay rate inside computer rooms.")]
    [SerializeField] private float computerRoomDecayRate = 3f;

    [Header("Insanity Rates — Increase (per second)")]
    [Tooltip("Passive insanity added every second while the Siren Phase is active.")]
    [SerializeField] private float sirenPassiveRate = 4f;
    [Tooltip("Passive insanity added per second when player is in darkness for >10 seconds.")]
    [SerializeField] private float darknessRate = 2f;
    [Tooltip("Seconds of continuous darkness before the darkness rate kicks in.")]
    [SerializeField] private float darknessBuildupTime = 10f;

    [Header("Break Event")]
    [Tooltip("Insanity level that insanity resets to after a Break Event.")]
    [SerializeField] private float breakEventResetValue = 75f;
    [Tooltip("Seconds after a Break Event before The Presence can spawn again.")]
    [SerializeField] private float presencePostBreakBan = 30f;

    [Header("Level Carry-In Floors (%) — insanity on entering each level is at least this value)")]
    [SerializeField] private float[] levelCarryInFloors = { 0f, 10f, 20f, 30f };

    // Fired every frame the value changes, passing the 0-100 insanity.
    public event Action<float> OnInsanityChanged;
    // Fired when the stage index (0-3) changes.
    public event Action<int> OnStageChanged;
    // Fired when insanity hits 100.
    public event Action OnBreakEvent;
    // Fired after a break event, passing how long the Presence is banned for.
    public event Action<float> OnBreakEventComplete;

    private float insanity = 0f;
    public float Insanity => insanity;

    private int currentStage = 0;
    public int CurrentStage => currentStage;

    public bool IsPresenceActive { get; set; } = false;
    public bool IsSirenActive { get; set; } = false;

    [Header("Room Zone Detection")]
    [Tooltip("XZ radius around a room centre that counts as 'inside' it. Tile size = 4 units, so 2.5 covers the tile with a small margin and won't bleed into adjacent tiles.")]
    [SerializeField] private float roomProximityRadius = 2.5f;
    [Tooltip("How often (in seconds) the room proximity check runs. 0.2 = 5 times/sec — cheap enough.")]
    [SerializeField] private float roomCheckInterval   = 0.2f;
    [Tooltip("Vertical distance between dungeon levels. Must match ProceduralDungeonGenerator.levelHeight.")]
    [SerializeField] private float levelHeight = 4f;

    // Room zone tracking — updated by periodic proximity check, no triggers needed
    private RoomZoneType currentRoomZone = RoomZoneType.None;

    // Cached room setup references (populated once after dungeon generates)
    private SafeRoomSetup     safeRoomSetup;
    private SpawnRoomSetup    spawnRoomSetup;
    private ComputerRoomSetup computerRoomSetup;
    private HiddenRoomSetup   hiddenRoomSetup;
    private Transform         playerTransform;
    private float             roomCheckTimer = 0f;

    // Darkness tracking
    private float darknessTimer = 0f;
    private bool  isInDarkness  = false;

    // Break Event guard
    private bool breakEventInProgress = false;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (breakEventInProgress) return;

        // ── Lazy-find player and room setups (dungeon generates after this Awake) ─
        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
        }

        // Refresh room setup references periodically — they may not exist yet on first frames
        roomCheckTimer += Time.deltaTime;
        if (roomCheckTimer >= roomCheckInterval)
        {
            roomCheckTimer = 0f;
            RefreshRoomSources();
            UpdateRoomZone();
        }

        float delta = 0f;

        // ── Decay (negative delta) ─────────────────────────────────────────────
        switch (currentRoomZone)
        {
            case RoomZoneType.SafeRoom:     delta -= safeRoomDecayRate     * Time.deltaTime; break;
            case RoomZoneType.SpawnRoom:    delta -= spawnRoomDecayRate    * Time.deltaTime; break;
            case RoomZoneType.HiddenRoom:   delta -= hiddenRoomDecayRate   * Time.deltaTime; break;
            case RoomZoneType.ComputerRoom: delta -= computerRoomDecayRate * Time.deltaTime; break;
            default:
                // Natural decay only applies when Presence is not active
                if (!IsPresenceActive)
                    delta -= naturalDecayRate * Time.deltaTime;
                break;
        }

        // ── Increase sources (automatic per-frame) ─────────────────────────────
        if (IsSirenActive)
            delta += sirenPassiveRate * Time.deltaTime;

        // Darkness buildup
        if (isInDarkness)
        {
            darknessTimer += Time.deltaTime;
            if (darknessTimer >= darknessBuildupTime)
                delta += darknessRate * Time.deltaTime;
        }
        else
        {
            darknessTimer = 0f;
        }

        if (Mathf.Approximately(delta, 0f)) return;

        ApplyDelta(delta);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Add (positive) or remove (negative) insanity. Clamped to 0–100.
    public void AddInsanity(float amount)
    {
        if (breakEventInProgress) return;
        ApplyDelta(amount);
    }

    public void SetInsanity(float value)
    {
        if (breakEventInProgress) return;
        float clamped = Mathf.Clamp(value, 0f, 100f);
        SetInsanityInternal(clamped);
    }

    // Called by GameManager on level change. Raises insanity to the floor for that level.
    public void OnLevelChanged(int newLevel)
    {
        if (levelCarryInFloors == null || newLevel <= 0) return;

        int idx   = Mathf.Clamp(newLevel, 0, levelCarryInFloors.Length - 1);
        float floor = levelCarryInFloors[idx];

        if (insanity < floor)
            SetInsanityInternal(floor);

        Debug.Log($"[InsanityManager] Level {newLevel} carry-in: insanity = {insanity:F1}% (floor was {floor}%)");
    }

    public void SetInDarkness(bool inDarkness) => isInDarkness = inDarkness;

    private void RefreshRoomSources()
    {
        if (safeRoomSetup     == null) safeRoomSetup     = FindObjectOfType<SafeRoomSetup>();
        if (spawnRoomSetup    == null) spawnRoomSetup    = FindObjectOfType<SpawnRoomSetup>();
        if (computerRoomSetup == null) computerRoomSetup = FindObjectOfType<ComputerRoomSetup>();
        if (hiddenRoomSetup   == null) hiddenRoomSetup   = FindObjectOfType<HiddenRoomSetup>();
    }

    // Picks which room the player is standing in. Runs on a timer, not every frame.
    private void UpdateRoomZone()
    {
        if (playerTransform == null) { currentRoomZone = RoomZoneType.None; return; }

        Vector3 pos = playerTransform.position;
        float r2    = roomProximityRadius * roomProximityRadius;

        // Check SafeRoom first so it wins if multiple radii overlap
        if (IsNearAny(pos, r2, safeRoomSetup?.GetSafeRoomCenters()))
            { currentRoomZone = RoomZoneType.SafeRoom;     return; }

        if (IsNearAny(pos, r2, spawnRoomSetup?.GetSpawnRoomPositions()))
            { currentRoomZone = RoomZoneType.SpawnRoom;    return; }

        if (IsNearAny(pos, r2, hiddenRoomSetup?.GetRoomCenters()))
            { currentRoomZone = RoomZoneType.HiddenRoom;   return; }

        if (IsNearAny(pos, r2, computerRoomSetup?.GetRoomCenters()))
            { currentRoomZone = RoomZoneType.ComputerRoom; return; }

        currentRoomZone = RoomZoneType.None;
    }

    // True if pos is within sqRadius of any centre on the player's current level.
    private bool IsNearAny(Vector3 pos, float sqRadius, IEnumerable<Vector3> centers)
    {
        if (centers == null) return false;

        // Room centres are stored with y = levelIndex * -levelHeight, so reverse that
        // to avoid matching rooms on other floors.
        int playerLevel = GameManager.Instance != null ? GameManager.Instance.GetCurrentLevel() : -1;

        foreach (Vector3 c in centers)
        {
            if (playerLevel >= 0)
            {
                int centerLevel = Mathf.RoundToInt(-c.y / levelHeight);
                if (centerLevel != playerLevel) continue;
            }

            Vector3 flat = new Vector3(pos.x - c.x, 0f, pos.z - c.z);
            if (flat.sqrMagnitude <= sqRadius)
                return true;
        }
        return false;
    }

    private void ApplyDelta(float delta)
    {
        float newValue = Mathf.Clamp(insanity + delta, 0f, 100f);
        SetInsanityInternal(newValue);
    }

    private void SetInsanityInternal(float newValue)
    {
        bool changed = !Mathf.Approximately(insanity, newValue);
        insanity = newValue;

        if (changed)
        {
            OnInsanityChanged?.Invoke(insanity);
            HUDManager.Instance?.UpdateInsanityBar(insanity / 100f);
            CheckStageChange();
        }

        // Break Event at 100%
        if (insanity >= 100f && !breakEventInProgress)
            StartCoroutine(TriggerBreakEvent());
    }

    private void CheckStageChange()
    {
        int newStage = InsanityStageFor(insanity);
        if (newStage != currentStage)
        {
            currentStage = newStage;
            OnStageChanged?.Invoke(currentStage);
            Debug.Log($"[InsanityManager] Stage changed → {currentStage} (insanity={insanity:F1}%)");
        }
    }

    private static int InsanityStageFor(float value)
    {
        if (value < 25f) return 0;
        if (value < 50f) return 1;
        if (value < 75f) return 2;
        return 3;
    }

    private IEnumerator TriggerBreakEvent()
    {
        breakEventInProgress = true;
        Debug.Log("[InsanityManager] BREAK EVENT triggered!");

        OnBreakEvent?.Invoke();

        // Brief pause so listeners can react to the event before we reset
        yield return new WaitForSeconds(0.5f);

        // Reset to 75% and re-check stage
        insanity = breakEventResetValue;
        OnInsanityChanged?.Invoke(insanity);
        HUDManager.Instance?.UpdateInsanityBar(insanity / 100f);
        CheckStageChange();

        OnBreakEventComplete?.Invoke(presencePostBreakBan);

        breakEventInProgress = false;
        Debug.Log($"[InsanityManager] Break Event complete — insanity reset to {insanity:F1}%");
    }
}

// ── Room Zone Type ─────────────────────────────────────────────────────────────

/// <summary>Room types that affect insanity decay rate.</summary>
public enum RoomZoneType
{
    None,
    SafeRoom,
    SpawnRoom,
    HiddenRoom,
    ComputerRoom
}
