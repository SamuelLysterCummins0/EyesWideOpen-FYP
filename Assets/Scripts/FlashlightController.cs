using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-held flashlight. Toggled by a button (default F).
/// Drains a battery while on; stuns PacerNPC and WatcherNPC inside the cone.
/// At insanity Stage 4 the flashlight flickers involuntarily (hand tremor).
///
/// Setup:
///   1. Attach to the Player GameObject (or its camera child).
///   2. Assign flashlightPrefab — a 3D flashlight model with a child Spotlight.
///      The model spawns at itemMountPoint when F is pressed and is destroyed when F is pressed again.
///   3. itemMountPoint is auto-found from HotbarManager.itemMountPoint; assign manually if needed.
///   4. Set lightIntensity to match the Intensity value on the prefab's Light component.
///   5. Set npcLayerMask to your NPC layer.
///   6. Assign batteryDrainRates per level in the Inspector (index = level number).
///   7. BatteryPickup calls AddBattery(amount) on this component.
/// </summary>
public class FlashlightController : MonoBehaviour
{
    public static FlashlightController Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The 3D flashlight model prefab spawned at the item mount when the flashlight is held. " +
             "Must have a child Light (Spotlight) — that light becomes the beam.")]
    [SerializeField] private GameObject flashlightPrefab;
    [Tooltip("Where the flashlight model appears (player's hand). Auto-found from HotbarManager if left null.")]
    [SerializeField] private Transform itemMountPoint;
    [Tooltip("GazeDetector — the flashlight beam rotates to follow the head-tracking cursor. Auto-found if left null.")]
    [SerializeField] private GazeDetector gazeDetector;
    [Tooltip("How smoothly the beam follows the gaze direction (higher = snappier).")]
    [SerializeField] private float gazeFollowSpeed = 15f;
    [Tooltip("LayerMask for NPC objects. Flashlight cone only tests against this mask.")]
    [SerializeField] private LayerMask npcLayerMask;
    [Tooltip("LayerMask for walls and obstacles. NPCs behind geometry on these layers are ignored by the flashlight.")]
    [SerializeField] private LayerMask wallLayerMask;

    [Header("Light Settings")]
    [Tooltip("Spotlight intensity at full battery. Matches the Intensity value on the prefab's Light component.")]
    [SerializeField] private float lightIntensity = 1f;

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;

    [Header("Battery")]
    [Tooltip("Battery drain per second while flashlight is on, indexed by level (0=level0, 1=level1, etc.).")]
    [SerializeField] private float[] batteryDrainRates = { 6f, 9f, 13f, 18f };
    [Tooltip("Battery recharge per second while flashlight is off (set 0 on later levels).")]
    [SerializeField] private float[] batteryRechargeRates = { 4f, 2f, 0f, 0f };
    [SerializeField] [Range(0f, 100f)] private float startingBattery = 100f;

    [Header("Cone Detection")]
    [Tooltip("Distance to check for NPCs inside the flashlight cone.")]
    [SerializeField] private float coneRange = 10f;
    [Tooltip("Half-angle of the spotlight cone used for NPC detection (should match spotLight.spotAngle / 2).")]
    [SerializeField] private float coneHalfAngle = 25f;

    [Header("Stun")]
    [Tooltip("Seconds the player must continuously hold the flashlight on a PacerNPC before the stun triggers.")]
    [SerializeField] private float stunHoldTime       = 2f;
    [SerializeField] private float stunDuration      = 3f;
    [SerializeField] private float stunImmunityWindow = 8f;

    [Header("Stage 4 Flicker")]
    [SerializeField] private float flickerInterval = 10f; // avg seconds between involuntary flickers
    [SerializeField] private float flickerDuration  = 0.5f;

    [Header("Unlock")]
    [Tooltip("If true the player can use the flashlight from the start. " +
             "If false they must pick up the FlashlightPickup item first.")]
    [SerializeField] private bool startUnlocked = true;

    // ── State ──────────────────────────────────────────────────────────────────

    private float battery;
    /// <summary>Battery level 0–100.</summary>
    public float Battery => battery;

    private bool isOn = false;
    public  bool IsOn => isOn;

    private bool isUnlocked = false;
    public  bool IsUnlocked => isUnlocked;

    // Runtime spotlight — lives on the spawned flashlight model, null when put away
    private Light      spotLight;
    private GameObject flashlightModel;

    // Intensity ceiling — set from lightIntensity at Start so DrainBattery can restore it
    private float defaultIntensity = 1f;

    private int  currentLevel     = 0;
    private bool isStage4Active   = false;
    private bool isDead           = false;
    private Coroutine flickerCoroutine;

    // Hold-to-stun: accumulate per-NPC flashlight exposure before triggering stun
    private readonly Dictionary<PacerNPC, float> _stunHoldTimers    = new Dictionary<PacerNPC, float>();
    private readonly List<PacerNPC>              _inConeThisFrame   = new List<PacerNPC>();
    private readonly List<PacerNPC>              _timerKeysToRemove = new List<PacerNPC>();

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private Camera playerCamera;

    private void Start()
    {
        defaultIntensity = lightIntensity;
        battery          = startingBattery;
        isUnlocked       = startUnlocked;

        playerCamera = Camera.main;

        if (gazeDetector == null)
            gazeDetector = FindObjectOfType<GazeDetector>();

        // Auto-find the item mount point from HotbarManager if not assigned in Inspector
        if (itemMountPoint == null && HotbarManager.Instance != null)
            itemMountPoint = HotbarManager.Instance.itemMountPoint;

        if (InsanityManager.Instance != null)
            InsanityManager.Instance.OnStageChanged += OnInsanityStageChanged;
    }

    private void OnDestroy()
    {
        if (InsanityManager.Instance != null)
            InsanityManager.Instance.OnStageChanged -= OnInsanityStageChanged;
    }

    private void Update()
    {
        if (isDead) return;

        // Toggle flashlight (only when unlocked — player must pick it up first)
        if (isUnlocked && Input.GetKeyDown(toggleKey))
            Toggle();

        // Always rotate the spotlight toward the gaze direction, even when off,
        // so it's already aimed correctly when toggled on
        UpdateGazeAim();

        if (isOn)
        {
            DrainBattery();
            CheckConeForNPCs();
        }
        else
        {
            RechargeBattery();
        }
    }

    /// <summary>
    /// Rotates the spotlight to follow the head-tracking gaze direction.
    /// The beam aims from its real world position (the mount point) toward the
    /// point in the world that the gaze ray is pointing at — this corrects the
    /// parallax offset that occurs because the flashlight is held to the side of
    /// the camera rather than directly at the eye.
    /// </summary>
    private void UpdateGazeAim()
    {
        if (spotLight == null || gazeDetector == null || playerCamera == null) return;
        if (!gazeDetector.IsTracking) return;

        Ray gazeRay = gazeDetector.GetGazeRay(playerCamera);

        // Find the world point the gaze ray is targeting.
        // If nothing is hit within 100 m, project a point far along the ray.
        Vector3 gazeTarget = Physics.Raycast(gazeRay, out RaycastHit hit, 100f)
            ? hit.point
            : gazeRay.origin + gazeRay.direction * 50f;

        // Aim the spotlight FROM its actual position (the held flashlight) TOWARD that target.
        // Using the raw gaze direction instead would offset the beam to match the mount point's
        // lateral position rather than the cursor's position on screen.
        Vector3 aimDir = (gazeTarget - spotLight.transform.position).normalized;

        Quaternion targetRotation = Quaternion.LookRotation(aimDir);
        spotLight.transform.rotation = Quaternion.Lerp(
            spotLight.transform.rotation,
            targetRotation,
            gazeFollowSpeed * Time.deltaTime);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void AddBattery(float amount)
    {
        battery = Mathf.Clamp(battery + amount, 0f, 100f);
        Debug.Log($"[Flashlight] Battery pickup: {battery:F1}%");
    }

    /// <summary>
    /// Called by FlashlightPickup when the player picks up the flashlight on the ground.
    /// Enables the F-key toggle.
    /// </summary>
    public void UnlockFlashlight()
    {
        isUnlocked = true;
        Debug.Log("[FlashlightController] Flashlight unlocked — press F to use.");
    }

    /// <summary>Called by GameManager.SetCurrentLevel to scale battery rates per level.</summary>
    public void SetLevel(int level)
    {
        currentLevel = Mathf.Clamp(level, 0, batteryDrainRates.Length - 1);

        // On level entry, set starting battery based on level difficulty
        float[] startBatteries = { 100f, 75f, 60f, 50f };
        battery = startBatteries[Mathf.Clamp(level, 0, startBatteries.Length - 1)];
    }

    public void SetDead(bool dead)
    {
        isDead = dead;
        if (dead)
        {
            SetLight(false);
            DespawnFlashlightModel();
        }
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void Toggle()
    {
        if (!isOn)
        {
            if (battery <= 0f) return; // Can't bring out the flashlight with a dead battery
            SpawnFlashlightModel();
            SetLight(true);
        }
        else
        {
            SetLight(false);
            DespawnFlashlightModel();
        }
    }

    // ── Flashlight model spawning ──────────────────────────────────────────────

    private void SpawnFlashlightModel()
    {
        if (flashlightPrefab == null)
        {
            Debug.LogWarning("[FlashlightController] flashlightPrefab not assigned.");
            return;
        }
        if (itemMountPoint == null)
        {
            Debug.LogWarning("[FlashlightController] itemMountPoint not found. Assign it or ensure HotbarManager is in the scene.");
            return;
        }

        DespawnFlashlightModel(); // clear any leftover model

        // Instantiate with no position/rotation override so the prefab's own transform
        // values are preserved. SetParent with worldPositionStays=false then re-interprets
        // those values as local offsets relative to the mount point, which keeps the model
        // at (0,0,0) local position and respects whatever rotation is set on the prefab.
        flashlightModel = Instantiate(flashlightPrefab);
        flashlightModel.transform.SetParent(itemMountPoint, false);

        // The model's child Light becomes the active spotlight
        spotLight = flashlightModel.GetComponentInChildren<Light>(true);
        if (spotLight != null)
            spotLight.intensity = defaultIntensity;
        else
            Debug.LogWarning("[FlashlightController] flashlightPrefab has no child Light component.");
    }

    private void DespawnFlashlightModel()
    {
        if (flashlightModel != null)
        {
            Destroy(flashlightModel);
            flashlightModel = null;
        }
        spotLight = null;
    }

    private void SetLight(bool on)
    {
        isOn = on;
        if (spotLight != null) spotLight.enabled = on;
    }

    private void DrainBattery()
    {
        float drainRate = batteryDrainRates.Length > currentLevel
            ? batteryDrainRates[currentLevel]
            : batteryDrainRates[batteryDrainRates.Length - 1];

        battery -= drainRate * Time.deltaTime;

        if (battery <= 0f)
        {
            battery = 0f;
            SetLight(false);
            Debug.Log("[Flashlight] Battery dead.");
        }
        else if (battery <= 20f && spotLight != null)
        {
            // Warning flicker at low battery — flickers between dim and full
            spotLight.intensity = Mathf.Lerp(defaultIntensity * 0.15f, defaultIntensity,
                                             Mathf.PingPong(Time.time * 8f, 1f));
        }
        else if (spotLight != null)
        {
            // Restore to the intensity set in the Inspector (not hardcoded 1)
            spotLight.intensity = defaultIntensity;
        }
    }

    private void RechargeBattery()
    {
        float rechargeRate = batteryRechargeRates.Length > currentLevel
            ? batteryRechargeRates[currentLevel]
            : 0f;

        if (rechargeRate > 0f)
            battery = Mathf.Clamp(battery + rechargeRate * Time.deltaTime, 0f, 100f);
    }

    private void CheckConeForNPCs()
    {
        if (spotLight == null) return;

        // Use spotLight position/forward so the cone follows the gaze-aimed beam, not the player body.
        // Exclude Ignore Raycast layer (2) so stairway triggers are skipped.
        Vector3 beamOrigin   = spotLight.transform.position;
        Vector3 beamForward  = spotLight.transform.forward;
        int     combinedMask = npcLayerMask & ~(1 << 2);
        Collider[] hits = Physics.OverlapSphere(beamOrigin, coneRange, combinedMask);

        _inConeThisFrame.Clear();

        foreach (Collider hit in hits)
        {
            Vector3 dirToNPC      = (hit.bounds.center - beamOrigin).normalized;
            float   distToNPC     = Vector3.Distance(beamOrigin, hit.bounds.center);
            float   angle         = Vector3.Angle(beamForward, dirToNPC);
            if (angle > coneHalfAngle) continue;

            // Line-of-sight check — skip NPCs occluded by walls.
            // Uses the collider's world-space centre so the ray aims at the body, not the feet.
            if (wallLayerMask != 0 &&
                Physics.Raycast(beamOrigin, dirToNPC, distToNPC, wallLayerMask))
                continue;

            // Walk up the hierarchy — the Collider may live on a child mesh object
            // while PacerNPC / NPCMovement are on the root.
            PacerNPC    pacer = hit.GetComponentInParent<PacerNPC>();
            NPCMovement angel = hit.GetComponentInParent<NPCMovement>();

            // PacerNPC: player must hold the light for stunHoldTime seconds before stun triggers
            if (pacer != null)
            {
                _inConeThisFrame.Add(pacer);

                if (!_stunHoldTimers.ContainsKey(pacer))
                    _stunHoldTimers[pacer] = 0f;

                _stunHoldTimers[pacer] += Time.deltaTime;

                if (_stunHoldTimers[pacer] >= stunHoldTime)
                {
                    pacer.TryStun(stunDuration, stunImmunityWindow);
                    _stunHoldTimers[pacer] = 0f; // reset so it can be re-stunned after immunity
                }
            }

            // NPCMovement (weeping angel): notify it so it builds exposure internally
            angel?.NotifyFlashlightHit();
        }

        // Drop hold timers for pacers that left the cone this frame or were destroyed
        _timerKeysToRemove.Clear();
        foreach (var kvp in _stunHoldTimers)
        {
            if (kvp.Key == null || !_inConeThisFrame.Contains(kvp.Key))
                _timerKeysToRemove.Add(kvp.Key);
        }
        foreach (PacerNPC p in _timerKeysToRemove)
            _stunHoldTimers.Remove(p);
    }

    private void OnInsanityStageChanged(int stage)
    {
        isStage4Active = stage >= 3;

        if (isStage4Active && flickerCoroutine == null)
            flickerCoroutine = StartCoroutine(InvoluntaryFlickerLoop());
        else if (!isStage4Active && flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
            flickerCoroutine = null;
        }
    }

    /// <summary>
    /// At insanity Stage 4, the flashlight flickers involuntarily every ~10 seconds.
    /// Represents hand tremor / psychological deterioration.
    /// </summary>
    private IEnumerator InvoluntaryFlickerLoop()
    {
        while (isStage4Active)
        {
            // Random interval around the base, so it doesn't feel mechanical
            float wait = flickerInterval * Random.Range(0.6f, 1.4f);
            yield return new WaitForSeconds(wait);

            if (!isOn || !isStage4Active) continue;

            // Brief involuntary cut
            SetLight(false);
            yield return new WaitForSeconds(flickerDuration);
            if (battery > 0f) SetLight(true);
        }

        flickerCoroutine = null;
    }
}
