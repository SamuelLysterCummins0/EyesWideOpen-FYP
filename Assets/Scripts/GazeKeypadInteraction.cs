using UnityEngine;
using TMPro;
using SUPERCharacter;

namespace NavKeypad
{
    public class GazeKeypadInteraction : MonoBehaviour
    {
        [Header("References")]
        private Keypad keypad;
        private SlidingDoor door;
        private Transform keypadCameraPosition;
        private GameObject player;

        [Header("Gaze Detection")]
        [SerializeField] private GazeDetector gazeDetector;
        [SerializeField] private BlinkDetector blinkDetector;

        [Header("UI")]
        [SerializeField] private GameObject interactionPrompt;
        [SerializeField] private TMP_Text promptText;

        [Header("Visual Feedback")]
        [SerializeField] private Color glowColor = Color.green;
        [SerializeField] private float glowIntensity = 2f;

        [Header("Settings")]
        [SerializeField] private float interactionDistance = 3f;
        [SerializeField] private float gazeStabilityTime = 0.5f;

        private Camera playerCamera;
        private SUPERCharacterAIO playerController;
        private bool isInteracting = false;
        private bool playerInRange = false;
        private bool isLookingAtKeypad = false;
        private float gazeLookTime = 0f;

        private Renderer[] keypadRenderers;
        private Color[] originalEmissionColors;

        private Vector3 originalCameraPosition;
        private Quaternion originalCameraRotation;
        private Transform cameraTransform;

        private bool wasBlinking = false;

        private void Awake()
        {
            playerCamera = Camera.main;
            cameraTransform = playerCamera.transform;

            keypad = GetComponent<Keypad>();
            door = transform.root.GetComponentInChildren<SlidingDoor>();

            if (door != null)
            {
                keypad.OnAccessGranted.AddListener(OpenDoor);
            }

            Transform camPos = transform.Find("CameraPosition");
            if (camPos != null) keypadCameraPosition = camPos;

            keypadRenderers = GetComponentsInChildren<Renderer>();
            originalEmissionColors = new Color[keypadRenderers.Length];

            for (int i = 0; i < keypadRenderers.Length; i++)
            {
                Material mat = keypadRenderers[i].material;
                mat.EnableKeyword("_EMISSION");

                if (mat.HasProperty("_EmissionColor"))
                {
                    originalEmissionColors[i] = mat.GetColor("_EmissionColor");
                }
                else
                {
                    originalEmissionColors[i] = Color.black;
                }
            }

            if (gazeDetector == null) gazeDetector = FindObjectOfType<GazeDetector>();
            if (blinkDetector == null) blinkDetector = FindObjectOfType<BlinkDetector>();

            Debug.Log($"Keypad interaction setup - GazeDetector: {gazeDetector != null}, BlinkDetector: {blinkDetector != null}");
        }

        private void Update()
        {
            if (player == null)
            {
                player = GameObject.FindGameObjectWithTag("Player");
                if (player == null) return;
                playerController = player.GetComponent<SUPERCharacterAIO>();
                Debug.Log("Player found!");
            }

            float distance = Vector3.Distance(transform.position, player.transform.position);
            playerInRange = distance <= interactionDistance;

            if (!isInteracting && playerInRange)
            {
                CheckGazeInteraction();
            }

            if (isInteracting)
            {
                HandleButtonPress();

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    EndInteraction();
                }
            }
        }

        private void CheckGazeInteraction()
        {
            if (!gazeDetector.IsTracking)
            {
                ResetGazeState();
                return;
            }

            Ray gazeRay = gazeDetector.GetGazeRay(playerCamera);

            if (Physics.Raycast(gazeRay, out RaycastHit hit, interactionDistance))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.gameObject == gameObject)
                {
                    if (!isLookingAtKeypad)
                    {
                        Debug.Log("Started looking at keypad!");
                    }

                    isLookingAtKeypad = true;
                    gazeLookTime += Time.deltaTime;

                    // Apply green glow effect with pulsing
                    float pulseValue = Mathf.PingPong(Time.time * 3f, 1f);
                    Color currentGlow = glowColor * glowIntensity * pulseValue;

                    foreach (Renderer renderer in keypadRenderers)
                    {
                        renderer.material.SetColor("_EmissionColor", currentGlow);
                    }

                    // Check for BLINK START (was not blinking, now is blinking)
                    bool isBlinkingNow = blinkDetector.IsBlinking;

                    if (isBlinkingNow && !wasBlinking)
                    {
                        Debug.Log($"BLINK START detected! Gaze time: {gazeLookTime:F2}s (need {gazeStabilityTime:F2}s)");

                        if (gazeLookTime >= gazeStabilityTime)
                        {
                            Debug.Log("Starting interaction!");
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
            if (isLookingAtKeypad)
            {
                Debug.Log("Stopped looking at keypad");
            }

            isLookingAtKeypad = false;
            gazeLookTime = 0f;
            wasBlinking = false;

            for (int i = 0; i < keypadRenderers.Length; i++)
            {
                keypadRenderers[i].material.SetColor("_EmissionColor", originalEmissionColors[i]);
            }
        }

        private void HandleButtonPress()
        {
            if (!gazeDetector.IsTracking) return;

            Ray gazeRay = gazeDetector.GetGazeRay(playerCamera);

            if (Physics.Raycast(gazeRay, out RaycastHit hit, 5f))
            {
                KeypadButton button = hit.collider.GetComponent<KeypadButton>();

                // Press button when BLINK STARTS (not blinking before, now blinking)
                if (button != null && blinkDetector.IsBlinking && !wasBlinking)
                {
                    Debug.Log("Pressed button via blink!");
                    button.PressButton();
                }
            }

            wasBlinking = blinkDetector.IsBlinking;
        }

        private void StartInteraction()
        {
            Debug.Log("=== STARTING KEYPAD INTERACTION ===");
            isInteracting = true;
            ResetGazeState();

            if (playerController != null)
            {
                playerController.enabled = false;
                Debug.Log("Player movement disabled");
            }

            if (keypadCameraPosition != null)
            {
                originalCameraPosition = cameraTransform.localPosition;
                originalCameraRotation = cameraTransform.localRotation;

                cameraTransform.position = keypadCameraPosition.position;
                cameraTransform.rotation = keypadCameraPosition.rotation;
                Debug.Log("Camera moved to keypad");
            }
        }

        private void EndInteraction()
        {
            Debug.Log("=== ENDING KEYPAD INTERACTION ===");
            isInteracting = false;

            if (playerController != null)
            {
                playerController.enabled = true;
            }

            cameraTransform.localPosition = originalCameraPosition;
            cameraTransform.localRotation = originalCameraRotation;
        }

        private void OpenDoor()
        {
            Debug.Log("Access granted! Opening door...");
            if (door != null)
            {
                door.OpenDoor();
            }

            Invoke(nameof(EndInteraction), 1f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactionDistance);
        }
    }
}