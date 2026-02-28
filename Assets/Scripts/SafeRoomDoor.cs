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

    private bool isOpen = false;
    private Vector3 closedLocalPos;
    private Vector3 openLocalPos;
    private Coroutine slideCoroutine;
    private Transform player;

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

        // Cache player once — avoids FindGameObjectWithTag every frame
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
    }

    private void Update()
    {
        // Simple keyboard fallback — replace with your gaze/raycast system as needed
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (player == null) return;

        if (Vector3.Distance(player.position, transform.position) <= interactDistance)
            Interact();
    }

    // Toggle the door open or closed.
    // Called by the player interaction system (raycast, trigger, or gaze).
    public void Interact()
    {
        isOpen = !isOpen;

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
