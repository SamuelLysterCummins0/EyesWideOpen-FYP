using UnityEngine;
using TMPro;
using SUPERCharacter;

namespace NavKeypad
{
    public class KeypadPlayerInteraction : MonoBehaviour
    {
        [Header("References")]
        private Keypad keypad; // Remove [SerializeField]
        private SlidingDoor door; // Remove [SerializeField]
        private Transform keypadCameraPosition; // Remove [SerializeField]
        private GameObject player; // Remove [SerializeField]

        [Header("UI")]
        [SerializeField] private GameObject interactionPrompt; // Keep [SerializeField]
        [SerializeField] private TMP_Text promptText; // Keep [SerializeField]

        [Header("Settings")]
        [SerializeField] private string promptMessage = "Press [E] to Interact";
        [SerializeField] private float interactionDistance = 3f;

        private Camera playerCamera;
        private SUPERCharacterAIO playerController;
        private bool isInteracting = false;
        private bool playerInRange = false;
        private Vector3 originalCameraPosition;
        private Quaternion originalCameraRotation;
        private Transform cameraTransform;

        private void Awake()
        {
            playerCamera = Camera.main;
            cameraTransform = playerCamera.transform;

            // Auto-find keypad on this GameObject
            keypad = GetComponent<Keypad>();

            // Find door - search entire prefab
            door = transform.root.GetComponentInChildren<SlidingDoor>();

            if (door != null)
            {
                Debug.Log($"Door found at runtime: {door.gameObject.name}");
                keypad.OnAccessGranted.AddListener(OpenDoor);
            }
            else
            {
                Debug.LogError("Could not find SlidingDoor anywhere in prefab!");
            }

            // Find camera position child
            Transform camPos = transform.Find("CameraPosition");
            if (camPos != null)
                keypadCameraPosition = camPos;
        }

        public void SetDoor(SlidingDoor doorToSet)
        {
            door = doorToSet;
            Debug.Log($"Door manually set: {door.gameObject.name}");
        }

        private void Update()
        {
            // Debug check
            if (player == null)
            {
                // Try to find player if not assigned
                player = GameObject.FindGameObjectWithTag("Player");
                if (player == null) return;

                playerController = player.GetComponent<SUPERCharacterAIO>();
                Debug.Log("Player found and assigned to keypad!");
            }

            // Check distance to player
            float distance = Vector3.Distance(transform.position, player.transform.position);
            playerInRange = distance <= interactionDistance;

            // Debug logging
            if (playerInRange && !isInteracting)
            {
                Debug.Log($"Player in range! Distance: {distance} - Press E to interact");
            }

            // Handle E key press
            if (playerInRange && !isInteracting && Input.GetKeyDown(KeyCode.E))
            {
                Debug.Log("E pressed! Starting interaction");
                StartInteraction();
            }

            // Handle exit
            if (isInteracting && Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("ESC pressed! Ending interaction");
                EndInteraction();
            }
        }

        private void StartInteraction()
        {
            Debug.Log("=== STARTING KEYPAD INTERACTION ===");
            isInteracting = true;

            // Disable player movement
            if (playerController != null)
            {
                playerController.enabled = false;
                Debug.Log("Player movement disabled");
            }
            else
            {
                Debug.LogWarning("Player controller is null!");
            }

            // Move camera to keypad view
            if (keypadCameraPosition != null)
            {
                originalCameraPosition = cameraTransform.localPosition;
                originalCameraRotation = cameraTransform.localRotation;

                cameraTransform.position = keypadCameraPosition.position;
                cameraTransform.rotation = keypadCameraPosition.rotation;
                Debug.Log("Camera moved to keypad");
            }
            else
            {
                Debug.LogWarning("Camera position is null!");
            }

            // Show and unlock cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("Cursor unlocked");
        }

        private void EndInteraction()
        {
            isInteracting = false;

            // Re-enable player movement
            if (playerController != null)
            {
                playerController.enabled = true;
            }

            // Restore camera position
            cameraTransform.localPosition = originalCameraPosition;
            cameraTransform.localRotation = originalCameraRotation;

            // Hide and lock cursor
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OpenDoor()
        {
            Debug.Log($"=== OpenDoor called - door is: {(door != null ? door.gameObject.name : "NULL")} ===");

            if (door != null)
            {
                Debug.Log($"Door found: {door.gameObject.name}");
                door.OpenDoor();
                Debug.Log("Door.OpenDoor() called");
            }
            else
            {
                Debug.LogError("Door is NULL! Cannot open door.");
            }

            // Auto-exit after successful entry
            Invoke(nameof(EndInteraction), 1f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactionDistance);
        }
    }
}