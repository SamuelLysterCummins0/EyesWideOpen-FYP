using System.Collections;
using UnityEngine;
using SUPERCharacter;

/// <summary>
/// Controls the locker hiding mechanic using the same gaze+blink pattern as keypads,
/// goggles, and the power box.
///
/// States:
///   Outside (nearby) → look at locker (gaze cursor) → cyan pulse glow → blink to enter
///   Inside            → prompt "Blink to exit"       → blink to exit
///
/// While Inside:
///   • Player controller and main camera are disabled; LockerCamera is enabled.
///   • Player is SAFE — NPCs cannot trigger a jumpscare.
///   • Insanity decays at insanityDecayRate per second.
///   • NPC proximity polling: if an NPC lingers within tensionRadius for npcShuffleDelay
///     seconds, it is repositioned (same logic as RoomNPCShuffle / SafeRoomDoor).
///   • Heartbeat responds naturally to NPC distance, and resets when NPCs shuffle away.
///
/// Setup:
///   1. Attach to the locker root GameObject (needs a trigger Collider for the proximity zone).
///   2. Add a child GameObject named "LockerCamera" with a Camera component (FOV ~25).
///   3. Assign interactionPromptUI and promptText in the Inspector (or leave null for no UI).
///   4. Set npcLayerMask to your NPC layer.
///   5. GazeDetector, BlinkDetector, BlinkVignetteController, and SUPERCharacterAIO
///      auto-found in Start if left null.
/// </summary>
public class LockerInteraction : MonoBehaviour
{
    // ── Singleton-like safety flag — checked by NPCMovement.OnTriggerEnter ──────
    /// <summary>True while any locker has the player inside it. NPCs skip their kill trigger while this is set.</summary>
    public static bool IsHidingInLocker { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────────

    [Header("Cameras")]
    [Tooltip("Child camera showing the inside-locker vent view. Narrow FOV (~25) recommended.")]
    [SerializeField] private Camera lockerCamera;

    [Header("Gaze / Blink — Entry")]
    [Tooltip("Max distance at which the player can gaze-enter this locker.")]
    [SerializeField] private float rayDistance       = 4f;
    [Tooltip("SphereCast radius for tolerance when aiming the gaze cursor at the locker.")]
    [SerializeField] private float gazeHitRadius     = 0.35f;
    [SerializeField] private Color gazeGlowColor     = new Color(0f, 0.85f, 1f); // cyan
    [SerializeField] private float gazeGlowIntensity = 2.2f;
    [SerializeField] private float gazeGlowPulseSpeed= 3f;

    [Header("NPC Detection")]
    [Tooltip("Radius around the locker that triggers NPC awareness when an NPC enters.")]
    [SerializeField] private float tensionRadius = 2.5f;
    [SerializeField] private LayerMask npcLayerMask;

    [Header("Locker Safety")]
    [Tooltip("Insanity removed per second while the player is hiding in this locker.")]
    [SerializeField] private float insanityDecayRate = 3f;
    [Tooltip("Seconds an NPC must linger nearby before being shuffled to a new position (same as safe room door logic).")]
    [SerializeField] private float npcShuffleDelay = 5f;
    [Tooltip("Cooldown after a shuffle before the next shuffle can trigger.")]
    [SerializeField] private float npcShuffleCooldown = 10f;
    [Tooltip("Radius around the locker within which NPCs are candidates for repositioning. Match this to how far you want NPCs pushed away.")]
    [SerializeField] private float npcShuffleRadius = 8f;

    [Header("Prompt UI")]
    [Tooltip("Optional UI panel to show/hide.")]
    [SerializeField] private GameObject interactionPromptUI;
    [Tooltip("TMP_Text component that displays the interaction hint.")]
    [SerializeField] private TMPro.TMP_Text promptText;

    [Header("References (auto-found if left null)")]
    [SerializeField] private BlinkVignetteController vignetteController;
    [SerializeField] private SUPERCharacterAIO        playerController;
    [SerializeField] private GazeDetector             gazeDetector;
    [SerializeField] private BlinkDetector            blinkDetector;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool isInLocker    = false;
    private bool playerNearby  = false;
    private bool isEntering    = false; // lockout guard — prevents entry-blink triggering exit

    // NPC shuffle timers (mirrors RoomNPCShuffle pattern)
    private float shuffleTimer         = 0f;
    private float shuffleCooldownTimer = 0f;

    // ── Gaze Glow ──────────────────────────────────────────────────────────────

    private bool       isGazedAt        = false;
    private bool       wasBlinking       = false;
    private Renderer[] lockerRenderers;
    private Color[]    originalEmission;
    private Color[]    originalBaseColor;
    private bool[]     supportsEmission;
    private bool[]     usesBaseColor;

    // ── Components ─────────────────────────────────────────────────────────────

    private Camera              mainCamera;
    private GameManager         gameManager;
    private Transform           playerTransform;
    private CharacterController characterController;

    // Where the player was standing before entering — restored on exit
    private Vector3 entryPosition;

    // Main camera state cached before entering, so it can be fully restored on exit
    private Transform  cameraOriginalParent;
    private Vector3    cameraOriginalLocalPos;
    private Quaternion cameraOriginalLocalRot;
    private float      cameraOriginalFOV;

    // ── Unity ──────────────────────────────────────────────────────────────────

    private void Awake()
    {
        mainCamera = Camera.main;

        // Auto-find LockerCamera child
        if (lockerCamera == null)
        {
            Transform camChild = transform.Find("LockerCamera");
            if (camChild != null) lockerCamera = camChild.GetComponent<Camera>();
        }
        if (lockerCamera != null) lockerCamera.enabled = false;

        if (interactionPromptUI != null) interactionPromptUI.SetActive(false);

        // Cache per-instance material copies for glow
        lockerRenderers   = GetComponentsInChildren<Renderer>();
        originalEmission  = new Color[lockerRenderers.Length];
        originalBaseColor = new Color[lockerRenderers.Length];
        supportsEmission  = new bool [lockerRenderers.Length];
        usesBaseColor     = new bool [lockerRenderers.Length];

        for (int i = 0; i < lockerRenderers.Length; i++)
        {
            Material mat = lockerRenderers[i].material;

            supportsEmission[i] = mat.HasProperty("_EmissionColor");
            if (supportsEmission[i])
            {
                mat.EnableKeyword("_EMISSION");
                originalEmission[i] = mat.GetColor("_EmissionColor");
            }

            if (mat.HasProperty("_BaseColor"))
            {
                usesBaseColor[i]     = true;
                originalBaseColor[i] = mat.GetColor("_BaseColor");
            }
            else if (mat.HasProperty("_Color"))
            {
                usesBaseColor[i]     = false;
                originalBaseColor[i] = mat.GetColor("_Color");
            }
            else
            {
                originalBaseColor[i] = Color.white;
            }
        }
    }

    private void Start()
    {
        gameManager     = GameManager.Instance;
        playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (vignetteController == null) vignetteController = FindObjectOfType<BlinkVignetteController>();
        if (gazeDetector       == null) gazeDetector       = FindObjectOfType<GazeDetector>();
        if (blinkDetector      == null) blinkDetector      = FindObjectOfType<BlinkDetector>();

        if (playerController == null && playerTransform != null)
            playerController = playerTransform.GetComponent<SUPERCharacterAIO>();

        if (playerTransform != null)
            characterController = playerTransform.GetComponent<CharacterController>();
    }

    private void Update()
    {
        // ── Outside: gaze at locker → blink to enter ───────────────────────────
        if (!isInLocker && !isEntering)
        {
            if (playerNearby)
            {
                isGazedAt = CheckGaze();

                if (isGazedAt)
                {
                    PulseGlow();
                    ShowPrompt("Blink to hide");
                    bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
                    if (blinkingNow && !wasBlinking)
                        StartCoroutine(EnterLocker());
                    wasBlinking = blinkingNow;
                }
                else
                {
                    ResetGlow();
                    HidePrompt();
                    wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
                }
            }
            else
            {
                ResetGlow();
                HidePrompt();
                wasBlinking = blinkDetector != null && blinkDetector.IsBlinking;
            }
            return;
        }

        // ── Inside: safe zone ──────────────────────────────────────────────────
        if (isInLocker && !isEntering)
        {
            // Decay insanity while hiding
            InsanityManager.Instance?.AddInsanity(-insanityDecayRate * Time.deltaTime);

            // Check for nearby NPCs and shuffle them away after a delay
            UpdateNPCShuffleTimer();

            // Blink to exit (always available — player is safe here)
            bool blinkingNow = blinkDetector != null && blinkDetector.IsBlinking;
            if (blinkingNow && !wasBlinking)
                StartCoroutine(ExitLocker());
            wasBlinking = blinkingNow;
        }
    }

    // ── Trigger Zone (player proximity) ───────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerNearby = false;
        ResetGlow();
        HidePrompt();
    }

    // ── Gaze Detection ─────────────────────────────────────────────────────────

    private bool CheckGaze()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking || mainCamera == null)
            return false;

        Ray ray = gazeDetector.GetGazeRay(mainCamera);

        // Use Ignore so the locker's own proximity trigger zone (a large box trigger) is
        // never counted as a gaze hit — only solid non-trigger geometry registers.
        // This matches the PowerboxInteraction / GazeKeypadInteraction pattern.
        if (Physics.Raycast(ray, out RaycastHit directHit, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (directHit.collider.transform == transform ||
                directHit.collider.transform.IsChildOf(transform))
                return true;
        }

        RaycastHit[] hits = Physics.SphereCastAll(ray, gazeHitRadius, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit h in hits)
        {
            if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform))
                return true;
        }

        return false;
    }

    // ── Glow ───────────────────────────────────────────────────────────────────

    private void PulseGlow()
    {
        float pulse = Mathf.PingPong(Time.time * gazeGlowPulseSpeed, 1f);
        for (int i = 0; i < lockerRenderers.Length; i++)
        {
            Material m = lockerRenderers[i].material;
            if (supportsEmission[i])
                m.SetColor("_EmissionColor", gazeGlowColor * gazeGlowIntensity * pulse);
            Color tinted = Color.Lerp(originalBaseColor[i], gazeGlowColor, pulse * 0.4f);
            if (usesBaseColor[i])           m.SetColor("_BaseColor", tinted);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", tinted);
        }
    }

    private void ResetGlow()
    {
        for (int i = 0; i < lockerRenderers.Length; i++)
        {
            Material m = lockerRenderers[i].material;
            if (supportsEmission[i])      m.SetColor("_EmissionColor", originalEmission[i]);
            if (usesBaseColor[i])         m.SetColor("_BaseColor",     originalBaseColor[i]);
            else if (m.HasProperty("_Color")) m.SetColor("_Color",     originalBaseColor[i]);
        }
    }

    // ── Enter / Exit ───────────────────────────────────────────────────────────

    private IEnumerator EnterLocker()
    {
        isEntering = true;
        isInLocker = true;
        IsHidingInLocker = true;
        ResetGlow();
        HidePrompt();

        if (playerController != null) playerController.enabled = false;

        // Teleport the player inside the locker so NPCs lose their target position.
        // CharacterController must be disabled before setting transform.position.
        if (playerTransform != null)
        {
            entryPosition = playerTransform.position;
            if (characterController != null) characterController.enabled = false;
            playerTransform.position = transform.position;
        }

        // Reposition the MAIN camera to the locker view instead of switching cameras.
        // This keeps all post-processing (InsanityVFX, bloom, global volumes, blink vignette,
        // siren phase effects) active because it is the same camera object — just moved.
        if (mainCamera != null && lockerCamera != null)
        {
            cameraOriginalParent   = mainCamera.transform.parent;
            cameraOriginalLocalPos = mainCamera.transform.localPosition;
            cameraOriginalLocalRot = mainCamera.transform.localRotation;
            cameraOriginalFOV      = mainCamera.fieldOfView;

            mainCamera.transform.SetParent(null, true); // detach so player movement can't drag it
            mainCamera.transform.position = lockerCamera.transform.position;
            mainCamera.transform.rotation = lockerCamera.transform.rotation;
            mainCamera.fieldOfView        = lockerCamera.fieldOfView;
            // mainCamera stays ENABLED — no camera switch needed
        }

        Debug.Log("[LockerInteraction] Entered locker — player is safe.");

        wasBlinking = true;
        yield return new WaitForSeconds(0.6f);
        wasBlinking = false;
        isEntering  = false;
    }

    private IEnumerator ExitLocker()
    {
        isInLocker       = false;
        IsHidingInLocker = false;
        shuffleTimer         = 0f;
        shuffleCooldownTimer = 0f;

        // Restore the camera to its original parent and local transform
        if (mainCamera != null && cameraOriginalParent != null)
        {
            mainCamera.transform.SetParent(cameraOriginalParent, false);
            mainCamera.transform.localPosition = cameraOriginalLocalPos;
            mainCamera.transform.localRotation = cameraOriginalLocalRot;
            mainCamera.fieldOfView             = cameraOriginalFOV;
        }

        // Place the player in front of the locker door on the NavMesh, then re-enable movement.
        if (playerTransform != null)
        {
            Vector3 exitPos = transform.position + transform.forward * 1.5f;
            UnityEngine.AI.NavMeshHit navHit;
            if (UnityEngine.AI.NavMesh.SamplePosition(exitPos, out navHit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                exitPos = navHit.position;

            playerTransform.position = exitPos;
            if (characterController != null) characterController.enabled = true;
        }

        if (playerController != null) playerController.enabled = true;

        ResetGlow();
        HidePrompt();
        wasBlinking = false;
        Debug.Log("[LockerInteraction] Exited locker.");
        yield return null;
    }

    // ── NPC Shuffle (mirrors RoomNPCShuffle logic) ─────────────────────────────

    /// <summary>
    /// While the player is inside, track how long a nearby NPC has been lingering.
    /// After npcShuffleDelay seconds of continuous proximity, call ShuffleNearbyNPCs —
    /// the same repositioning used by RoomNPCShuffle / SafeRoomDoor.
    /// </summary>
    private void UpdateNPCShuffleTimer()
    {
        if (shuffleCooldownTimer > 0f)
        {
            shuffleCooldownTimer -= Time.deltaTime;
            ShowPrompt("Blink to exit");
            return;
        }

        bool npcClose = IsAnyNPCNearby();

        if (npcClose)
        {
            shuffleTimer += Time.deltaTime;
            int secsLeft = Mathf.CeilToInt(npcShuffleDelay - shuffleTimer);
            ShowPrompt(secsLeft > 0 ? $"Stay still... {secsLeft}s" : "Stay still...");

            if (shuffleTimer >= npcShuffleDelay)
            {
                ShuffleNearbyNPCs();
                shuffleTimer         = 0f;
                shuffleCooldownTimer = npcShuffleCooldown;
                Debug.Log("[LockerInteraction] NPC shuffled away from locker.");
            }
        }
        else
        {
            shuffleTimer = 0f;
            ShowPrompt("Blink to exit");
        }
    }

    private bool IsAnyNPCNearby()
    {
        // NPC colliders are trigger volumes (set by NPCSpawnManager.SetupNPC), so we must use
        // QueryTriggerInteraction.Collide — Ignore would miss them entirely.
        // Fall back to all layers (except IgnoreRaycast) when npcLayerMask isn't configured.
        int mask = npcLayerMask.value != 0 ? npcLayerMask.value : ~(1 << 2);

        // Direct: NPC inside tensionRadius
        Collider[] hits = Physics.OverlapSphere(transform.position, tensionRadius,
                                                mask, QueryTriggerInteraction.Collide);
        if (hits.Length > 0) return true;

        // Approaching: NPC within shuffle radius but NavMesh path brings it closer
        Collider[] wider = Physics.OverlapSphere(transform.position, npcShuffleRadius,
                                                 mask, QueryTriggerInteraction.Collide);
        foreach (Collider c in wider)
        {
            UnityEngine.AI.NavMeshAgent nma = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nma != null && nma.remainingDistance <= tensionRadius)
                return true;
        }
        return false;
    }

    private void ShuffleNearbyNPCs()
    {
        // Find lazily — NPCSpawnManager instances are created dynamically during dungeon
        // generation and won't exist yet if we cache them in Start().
        foreach (NPCSpawnManager manager in FindObjectsOfType<NPCSpawnManager>())
            manager.ShuffleNearbyNPCs(transform.position, npcShuffleRadius);
    }

    // ── UI Helpers ─────────────────────────────────────────────────────────────

    private void ShowPrompt(string message)
    {
        if (interactionPromptUI != null) interactionPromptUI.SetActive(true);
        if (promptText          != null) promptText.text = message;
    }

    private void HidePrompt()
    {
        if (interactionPromptUI != null) interactionPromptUI.SetActive(false);
    }

    // ── Gizmos ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, tensionRadius);
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, tensionRadius * 2f);
    }
}
