using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Attach to each safe room door prefab.
// Call Interact() from the player's interaction system (raycast hit or trigger).
// When closed: NavMeshObstacle carves the NavMesh, blocking NPC pathfinding through the door.
// When open:   obstacle disabled, NPCs can path through normally.
public class SafeRoomDoor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The NavMeshObstacle on the door panel — enables/disables to block NPCs")]
    [SerializeField] private NavMeshObstacle obstacle;

    [Tooltip("The visual door panel that slides up/down")]
    [SerializeField] private Transform doorPanel;

    [Header("Settings")]
    [Tooltip("How far the door panel slides upward when opened")]
    [SerializeField] private float slideDistance = 3f;

    [Tooltip("How long the open/close slide takes in seconds")]
    [SerializeField] private float slideSpeed = 0.3f;

    [Tooltip("Show a prompt when the player is close enough to interact")]
    [SerializeField] private string interactPrompt = "Press E to open/close door";

    [Tooltip("Max distance for the player to interact with the door")]
    [SerializeField] private float interactDistance = 3f;

    [Header("Audio")]
    [Tooltip("Played when the door opens (slides up).")]
    [SerializeField] private AudioClip openSound;
    [Tooltip("Played when the door closes (slides down). Uses openSound if left null.")]
    [SerializeField] private AudioClip closeSound;
    [SerializeField] private AudioSource audioSource;

    private bool isOpen = false;
    private Vector3 closedLocalPos;
    private Vector3 openLocalPos;
    private Coroutine slideCoroutine;
    private Transform player;
    private Collider interactionHitbox;

    private void Start()
    {
        if (doorPanel == null)
        {
            Debug.LogWarning($"SafeRoomDoor on {name}: doorPanel not assigned.");
            return;
        }

        closedLocalPos = doorPanel.localPosition;
        openLocalPos   = closedLocalPos + Vector3.up * slideDistance;

        if (obstacle != null)
            obstacle.enabled = true;

        // Auto-find or create an AudioSource for door sounds.
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f; // 3D — volume falls off with distance
        audioSource.playOnAwake  = false;

        // Cache player once — avoids FindGameObjectWithTag every frame
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        // Add a dedicated trigger hitbox for gaze interaction without blocking movement.
        EnsureInteractionHitbox();

        // If a collider exists on the door root object, keep it as trigger so it never blocks passage.
        Collider rootCollider = GetComponent<Collider>();
        if (rootCollider != null)
        {
            rootCollider.isTrigger = true;
        }
    }

    private void EnsureInteractionHitbox()
    {
        Transform existing = transform.Find("DoorInteractHitbox");
        if (existing != null)
        {
            interactionHitbox = existing.GetComponent<Collider>();
            if (interactionHitbox != null)
            {
                interactionHitbox.isTrigger = true;
            }
            return;
        }

        GameObject hitboxObj = new GameObject("DoorInteractHitbox");
        hitboxObj.transform.SetParent(transform, false);
        hitboxObj.transform.localPosition = Vector3.zero;
        hitboxObj.transform.localRotation = Quaternion.identity;

        BoxCollider hitbox = hitboxObj.AddComponent<BoxCollider>();
        hitbox.size = new Vector3(2f, 3f, 0.3f);
        hitbox.isTrigger = true;
        interactionHitbox = hitbox;
    }

    // Toggle the door open or closed.
    // Called by the player interaction system (raycast, trigger, or gaze).
    public void Interact()
    {
        isOpen = !isOpen;

        // Play the appropriate sound — opening or closing.
        if (audioSource != null)
        {
            AudioClip clip = isOpen ? openSound : (closeSound != null ? closeSound : openSound);
            if (clip != null)
                audioSource.PlayOneShot(clip);
        }

        // Slide the door panel
        if (doorPanel != null)
        {
            if (slideCoroutine != null)
                StopCoroutine(slideCoroutine);
            slideCoroutine = StartCoroutine(SlideDoor(isOpen ? openLocalPos : closedLocalPos));
        }

        // Toggle NavMesh obstacle — only active (blocking) when door is closed
        if (obstacle != null)
            obstacle.enabled = !isOpen;
    }

    public bool IsOpen => isOpen;

    private IEnumerator SlideDoor(Vector3 targetLocalPos)
    {
        Vector3 startPos = doorPanel.localPosition;
        float elapsed = 0f;

        while (elapsed < slideSpeed)
        {
            elapsed += Time.deltaTime;
            doorPanel.localPosition = Vector3.Lerp(startPos, targetLocalPos, elapsed / slideSpeed);
            yield return null;
        }

        doorPanel.localPosition = targetLocalPos;
        slideCoroutine = null;
    }

    // Optional: draw a gizmo to visualise the interact radius in the editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}
