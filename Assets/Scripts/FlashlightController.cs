using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Player flashlight. Toggled with F. Drains battery while on, stuns NPCs in the cone.
public class FlashlightController : MonoBehaviour
{
    public static FlashlightController Instance { get; private set; }

    [Header("References")]
    [Tooltip("3D flashlight model. Must have a child Spotlight.")]
    [SerializeField] private GameObject flashlightPrefab;
    [Tooltip("Where the flashlight model sits in the player's hand.")]
    [SerializeField] private Transform itemMountPoint;
    [SerializeField] private GazeDetector gazeDetector;
    [Tooltip("How fast the beam follows the gaze cursor.")]
    [SerializeField] private float gazeFollowSpeed = 15f;
    [SerializeField] private LayerMask npcLayerMask;
    [Tooltip("Walls that block the flashlight beam.")]
    [SerializeField] private LayerMask wallLayerMask;

    [Header("Light Settings")]
    [SerializeField] private float lightIntensity = 1f;

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;

    [Header("Battery")]
    [Tooltip("Battery drain per second, one entry per level.")]
    [SerializeField] private float[] batteryDrainRates = { 6f, 9f, 13f, 18f };
    [Tooltip("Battery recharge per second while off, one entry per level.")]
    [SerializeField] private float[] batteryRechargeRates = { 4f, 2f, 0f, 0f };
    [SerializeField] [Range(0f, 100f)] private float startingBattery = 100f;

    [Header("Cone Detection")]
    [SerializeField] private float coneRange = 10f;
    [Tooltip("Half-angle of the cone used to detect NPCs in the beam.")]
    [SerializeField] private float coneHalfAngle = 25f;

    [Header("Stun")]
    [Tooltip("How long the beam must stay on a Pacer before stunning.")]
    [SerializeField] private float stunHoldTime       = 2f;
    [SerializeField] private float stunDuration      = 3f;
    [SerializeField] private float stunImmunityWindow = 8f;

    [Header("Stage 4 Flicker")]
    [SerializeField] private float flickerInterval = 10f;
    [SerializeField] private float flickerDuration  = 0.5f;

    [Header("Unlock")]
    [Tooltip("If false, the player must pick up the flashlight first.")]
    [SerializeField] private bool startUnlocked = true;

    private float battery;
    public float Battery => battery;

    private bool isOn = false;
    public  bool IsOn => isOn;

    private bool isUnlocked = false;
    public  bool IsUnlocked => isUnlocked;

    private Light      spotLight;
    private GameObject flashlightModel;
    private float defaultIntensity = 1f;

    private int  currentLevel     = 0;
    private bool isStage4Active   = false;
    private bool isDead           = false;
    private Coroutine flickerCoroutine;

    // Per-NPC timers so a Pacer only gets stunned once the beam has held on it long enough
    private readonly Dictionary<PacerNPC, float> _stunHoldTimers    = new Dictionary<PacerNPC, float>();
    private readonly List<PacerNPC>              _inConeThisFrame   = new List<PacerNPC>();
    private readonly List<PacerNPC>              _timerKeysToRemove = new List<PacerNPC>();

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

    // Rotates the spotlight to follow the gaze. Aims from the bulb toward the
    // world point the gaze ray is targeting so the beam lines up with where the
    // player is looking instead of with the camera centre.
    private void UpdateGazeAim()
    {
        if (spotLight == null || flashlightModel == null || gazeDetector == null || playerCamera == null) return;
        if (!gazeDetector.IsTracking) return;

        Ray gazeRay = gazeDetector.GetGazeRay(playerCamera);

        // Clamp a minimum distance so hits right at the player's feet don't sit
        // behind the bulb and flip the aim direction.
        const float minAimDistance = 3f;
        Vector3 gazeTarget;
        if (Physics.Raycast(gazeRay, out RaycastHit hit, 100f) && hit.distance >= minAimDistance)
            gazeTarget = hit.point;
        else
            gazeTarget = gazeRay.origin + gazeRay.direction * Mathf.Max(minAimDistance, 50f);

        Vector3 aimDir = (gazeTarget - spotLight.transform.position).normalized;
        if (aimDir.sqrMagnitude < 0.000001f) return;

        // Rotate the parent model so the child Light ends up pointed at the target.
        Quaternion desiredLightRot = Quaternion.LookRotation(aimDir);
        Quaternion lightLocalRot   = Quaternion.Inverse(flashlightModel.transform.rotation) * spotLight.transform.rotation;
        Quaternion targetRotation  = desiredLightRot * Quaternion.Inverse(lightLocalRot);

        flashlightModel.transform.rotation = Quaternion.Lerp(
            flashlightModel.transform.rotation,
            targetRotation,
            gazeFollowSpeed * Time.deltaTime);
    }

    public void AddBattery(float amount)
    {
        battery = Mathf.Clamp(battery + amount, 0f, 100f);
        Debug.Log($"[Flashlight] Battery pickup: {battery:F1}%");
    }

    // Called by FlashlightPickup once the player grabs the flashlight.
    public void UnlockFlashlight()
    {
        isUnlocked = true;
        Debug.Log("[FlashlightController] Flashlight unlocked — press F to use.");
    }

    // Called by GameManager to pick the drain/recharge rates for the current level.
    public void SetLevel(int level)
    {
        currentLevel = Mathf.Clamp(level, 0, batteryDrainRates.Length - 1);

        float[] startBatteries = { 100f, 75f, 60f, 50f };
        battery = startBatteries[Mathf.Clamp(level, 0, startBatteries.Length - 1)];
    }

    public void RefillBattery()
    {
        battery = 100f;
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

    private void Toggle()
    {
        if (!isOn)
        {
            if (battery <= 0f) return;
            SpawnFlashlightModel();
            SetLight(true);
        }
        else
        {
            SetLight(false);
            DespawnFlashlightModel();
        }
    }

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

        DespawnFlashlightModel();

        flashlightModel = Instantiate(flashlightPrefab);
        flashlightModel.transform.SetParent(itemMountPoint, false);

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
            // Low battery warning flicker
            spotLight.intensity = Mathf.Lerp(defaultIntensity * 0.15f, defaultIntensity,
                                             Mathf.PingPong(Time.time * 8f, 1f));
        }
        else if (spotLight != null)
        {
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

        // Cone follows the beam direction, not the player body. Skip Ignore Raycast layer.
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

            // Skip NPCs behind walls
            if (wallLayerMask != 0 &&
                Physics.Raycast(beamOrigin, dirToNPC, distToNPC, wallLayerMask))
                continue;

            // Collider may be on a child, so look up the hierarchy
            PacerNPC    pacer = hit.GetComponentInParent<PacerNPC>();
            NPCMovement angel = hit.GetComponentInParent<NPCMovement>();

            if (pacer != null)
            {
                _inConeThisFrame.Add(pacer);

                if (!_stunHoldTimers.ContainsKey(pacer))
                    _stunHoldTimers[pacer] = 0f;

                _stunHoldTimers[pacer] += Time.deltaTime;

                if (_stunHoldTimers[pacer] >= stunHoldTime)
                {
                    pacer.TryStun(stunDuration, stunImmunityWindow);
                    _stunHoldTimers[pacer] = 0f;
                }
            }

            angel?.NotifyFlashlightHit();
        }

        // Drop timers for pacers no longer in the cone
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

    // Involuntary flickers at insanity Stage 4.
    private IEnumerator InvoluntaryFlickerLoop()
    {
        while (isStage4Active)
        {
            float wait = flickerInterval * Random.Range(0.6f, 1.4f);
            yield return new WaitForSeconds(wait);

            if (!isOn || !isStage4Active) continue;

            SetLight(false);
            yield return new WaitForSeconds(flickerDuration);
            if (battery > 0f) SetLight(true);
        }

        flickerCoroutine = null;
    }
}
