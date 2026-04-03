using UnityEngine;
using UnityEngine.Events;
using SUPERCharacter;

public class ComputerInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GazeDetector gazeDetector;
    [SerializeField] private BlinkDetector blinkDetector;
    // MazeMinigame is found automatically at runtime — no need to assign in Inspector
    private MazeMinigame mazeMinigame;

    [Header("Screen")]
    [Tooltip("Empty child Transform placed flat on the screen surface, facing outward. The MazeCanvas snaps here at runtime.")]
    [SerializeField] private Transform screenFace;
    [SerializeField] private Renderer screenRenderer;
    [Tooltip("Which material slot is the screen in the Mesh Renderer? Check the Materials list in the Inspector — Element 0 = 0, Element 1 = 1, etc.")]
    [SerializeField] private int screenMaterialIndex = 0;
    [SerializeField] private Color glowColor = new Color(0f, 1f, 0.3f);
    [SerializeField] private Color activeScreenColor = new Color(0f, 0.9f, 0.2f);
    [SerializeField] private float glowIntensity = 2f;

    [Header("Settings")]
    [SerializeField] private float interactionDistance = 3f;
    [SerializeField] private float gazeStabilityTime = 0.5f;

    [Header("Events")]
    public UnityEvent OnMazeSolved;

    // --- Camera & Player ---
    private Camera playerCamera;
    private Transform cameraTransform;
    private SUPERCharacterAIO playerController;
    private GameObject player;
    private DoorWinkInteraction doorInteraction;

    private Transform computerCameraPosition;
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;

    // Set by ComputerRoomSetup after spawning so the maze knows which level's digit to reveal
    [HideInInspector] public int levelIndex = 0;

    // --- Per-computer maze state ---
    // Saved on first entry so every re-entry shows the same layout
    private bool[,] savedHWalls;
    private bool[,] savedVWalls;
    private bool    mazeDataSaved = false;
    // Set to true once the player completes this terminal — blocks all future access
    private bool    mazeCompleted = false;

    // --- State ---
    private bool isInteracting = false;
    private bool playerInRange = false;
    private bool isLookingAtComputer = false;
    private float gazeLookTime = 0f;
    private bool wasBlinking = false;

    private Material screenMaterial;      // cached instance of the screen's material slot
    private Color    originalEmissionColor;

    private void Awake()
    {
        playerCamera    = Camera.main;
        cameraTransform = playerCamera.transform;

        computerCameraPosition = transform.Find("CameraPosition");

        // Cache the specific material slot that is the screen face.
        // screenRenderer can be the same Renderer as the whole computer — we just target
        // the correct index from the Materials list shown in the Mesh Renderer Inspector.
        if (screenRenderer != null)
        {
            Material[] mats = screenRenderer.materials;
            int idx = Mathf.Clamp(screenMaterialIndex, 0, mats.Length - 1);
            screenMaterial = mats[idx];
            screenMaterial.EnableKeyword("_EMISSION");
            originalEmissionColor = screenMaterial.HasProperty("_EmissionColor")
                ? screenMaterial.GetColor("_EmissionColor")
                : Color.black;
        }

        if (gazeDetector == null)    gazeDetector    = FindObjectOfType<GazeDetector>();
        if (blinkDetector == null)   blinkDetector   = FindObjectOfType<BlinkDetector>();
        if (doorInteraction == null) doorInteraction = FindObjectOfType<DoorWinkInteraction>();
        // MazeMinigame is found lazily in StartInteraction() — not here —
        // because this Awake() fires during Instantiate() inside ComputerSpawner,
        // before Unity has finished registering all scene objects.

        Debug.Log($"ComputerInteraction setup - GazeDetector: {gazeDetector != null}, BlinkDetector: {blinkDetector != null}");
    }

    private void Update()
    {
        // Lazy-find player (may spawn after this script's Awake)
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;
            playerController = player.GetComponent<SUPERCharacterAIO>();
        }

        // Pre-warm MazeMinigame reference every frame until found —
        // ensures it is cached well before the player ever blinks,
        // avoiding a one-frame null on the very first StartInteraction() call
        if (mazeMinigame == null)
        {
            mazeMinigame = FindObjectOfType<MazeMinigame>(true);
            if (mazeMinigame != null)
                mazeMinigame.OnMazeSolvedCallback = OnMazeSolvedInternal;
        }

        float distance = Vector3.Distance(transform.position, player.transform.position);
        playerInRange = distance <= interactionDistance;

        if (!isInteracting && playerInRange)
        {
            if (!mazeCompleted)
                CheckGazeInteraction();
            else
                ResetGazeState(); // clear any residual screen glow
        }

        if (isInteracting)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndInteraction();
            }
        }
    }

    private void CheckGazeInteraction()
    {
        if (PowerManager.Instance != null && PowerManager.Instance.IsOutageLevelPoweredOff)
        {
            ResetGazeState();
            return;
        }

        if (gazeDetector == null || !gazeDetector.IsTracking)
        {
            ResetGazeState();
            return;
        }

        Ray gazeRay = gazeDetector.GetGazeRay(playerCamera);

        if (Physics.Raycast(gazeRay, out RaycastHit hit, interactionDistance))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.gameObject == gameObject)
            {
                isLookingAtComputer = true;
                gazeLookTime += Time.deltaTime;

                // Pulsing glow on screen — same pattern as GazeKeypadInteraction
                float pulseValue = Mathf.PingPong(Time.time * 3f, 1f);
                screenMaterial.SetColor("_EmissionColor", glowColor * glowIntensity * pulseValue);

                // Detect blink START (not blinking last frame, blinking now)
                bool isBlinkingNow = blinkDetector.IsBlinking;
                if (isBlinkingNow && !wasBlinking)
                {
                    if (gazeLookTime >= gazeStabilityTime)
                    {
                        Debug.Log("ComputerInteraction: Starting interaction via blink!");
                        StartInteraction();
                    }
                }
                wasBlinking = isBlinkingNow;
            }
            else
            {
                ResetGazeState();
            }
        }
        else
        {
            ResetGazeState();
        }
    }

    private void ResetGazeState()
    {
        isLookingAtComputer = false;
        gazeLookTime = 0f;
        wasBlinking = false;

        if (screenMaterial != null)
            screenMaterial.SetColor("_EmissionColor", originalEmissionColor);
    }

    private void StartInteraction()
    {
        // Final safety find — if pre-warm in Update() hasn't succeeded yet, try once more now
        if (mazeMinigame == null)
            mazeMinigame = FindObjectOfType<MazeMinigame>(true);

        if (mazeMinigame == null)
        {
            Debug.LogError("[ComputerInteraction] MazeMinigame not found — aborting interaction. Make sure MazeCanvas is in the scene.");
            return;
        }

        if (mazeCompleted)
        {
            Debug.Log("[ComputerInteraction] This terminal has already been completed — access denied.");
            return;
        }

        // Always re-register the callback in case it was lost (e.g. after a scene reload)
        mazeMinigame.OnMazeSolvedCallback = OnMazeSolvedInternal;

        Debug.Log("=== STARTING COMPUTER INTERACTION ===");
        isInteracting = true;
        ResetGazeState();

        // Disable door interaction so the gaze cursor can't trigger doors during the maze
        if (doorInteraction != null) doorInteraction.enabled = false;

        if (playerController != null)
            playerController.enabled = false;

        if (computerCameraPosition != null)
        {
            originalCameraPosition = cameraTransform.localPosition;
            originalCameraRotation = cameraTransform.localRotation;

            cameraTransform.position = computerCameraPosition.position;
            cameraTransform.rotation = computerCameraPosition.rotation;
            Debug.Log("Camera moved to computer screen");
        }

        // Turn screen on — bright active glow
        if (screenMaterial != null)
            screenMaterial.SetColor("_EmissionColor", activeScreenColor * glowIntensity);

        if (mazeMinigame != null)
        {
            mazeMinigame.mazeCamera = playerCamera;
            mazeMinigame.levelIndex = levelIndex;

            // Pass saved wall data so re-entry always shows the same maze.
            // On the very first entry savedHWalls is null, which tells StartMaze to generate fresh.
            mazeMinigame.StartMaze(screenFace, savedHWalls, savedVWalls);

            // Save the generated layout immediately after the first StartMaze call
            // so every subsequent entry (and every other computer) gets its own fixed layout.
            if (!mazeDataSaved)
            {
                savedHWalls   = CopyBoolArray(mazeMinigame.HWalls);
                savedVWalls   = CopyBoolArray(mazeMinigame.VWalls);
                mazeDataSaved = true;
            }
        }
    }

    private void EndInteraction()
    {
        Debug.Log("=== ENDING COMPUTER INTERACTION ===");
        isInteracting = false;

        if (mazeMinigame != null)
            mazeMinigame.StopMaze();

        if (playerController != null)
            playerController.enabled = true;

        // Re-enable door interaction now that the maze is closed
        if (doorInteraction != null) doorInteraction.enabled = true;

        // Guard against the camera transform being destroyed (e.g. when exiting Play mode)
        if (cameraTransform != null)
        {
            cameraTransform.localPosition = originalCameraPosition;
            cameraTransform.localRotation = originalCameraRotation;
        }

        if (screenMaterial != null)
            screenMaterial.SetColor("_EmissionColor", originalEmissionColor);
    }

    private void OnMazeSolvedInternal()
    {
        mazeCompleted = true; // lock this terminal permanently
        Debug.Log("Maze solved! Firing OnMazeSolved event.");
        OnMazeSolved.Invoke();
        Invoke(nameof(EndInteraction), 1.5f);
    }

    // Deep-copies a 2-D bool array so each ComputerInteraction holds its own independent layout
    private static bool[,] CopyBoolArray(bool[,] src)
    {
        if (src == null) return null;
        int w = src.GetLength(0), h = src.GetLength(1);
        bool[,] dst = new bool[w, h];
        System.Array.Copy(src, dst, src.Length);
        return dst;
    }

    private void OnDisable()
    {
        // Only call EndInteraction if the camera is still alive —
        // when exiting Play mode Unity destroys objects in undefined order
        // so cameraTransform may already be gone
        if (isInteracting && cameraTransform != null)
            EndInteraction();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionDistance);
    }
}
