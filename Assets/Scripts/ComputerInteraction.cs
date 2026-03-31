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

    private Transform computerCameraPosition;
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;

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

        if (gazeDetector == null)  gazeDetector  = FindObjectOfType<GazeDetector>();
        if (blinkDetector == null) blinkDetector = FindObjectOfType<BlinkDetector>();
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
            CheckGazeInteraction();
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
            return; // Do NOT proceed — prevents camera locking with no maze showing
        }

        // Always re-register the callback in case it was lost (e.g. after a scene reload)
        mazeMinigame.OnMazeSolvedCallback = OnMazeSolvedInternal;

        Debug.Log("=== STARTING COMPUTER INTERACTION ===");
        isInteracting = true;
        ResetGazeState();

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
            // Pass the camera AFTER it has moved, and the screen face transform so the
            // canvas repositions itself onto this specific computer's screen
            mazeMinigame.mazeCamera = playerCamera;
            mazeMinigame.StartMaze(screenFace);
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
        Debug.Log("Maze solved! Firing OnMazeSolved event.");
        OnMazeSolved.Invoke();
        Invoke(nameof(EndInteraction), 1.5f);
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
