using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to a safe room or spawn room entrance. After the player has been
/// inside with all doors closed for <see cref="shuffleDelay"/> seconds, any
/// NPCs lingering just outside the entrance are teleported to new valid
/// positions — the same repositioning behaviour used when the player dies.
///
/// Conditions that must ALL be true before the timer advances:
///   1. Player is within <see cref="playerInsideRadius"/> of the room centre.
///   2. Every door registered with this room reports IsOpen == false.
///   3. No cooldown is active from a previous shuffle.
/// </summary>
public class RoomNPCShuffle : MonoBehaviour
{
    [Tooltip("World-space centre of the room tile — set by Initialise().")]
    public Vector3 roomCenter;

    [Tooltip("Radius the player must be within to count as 'inside the room'.")]
    public float playerInsideRadius = 4f;

    [Tooltip("Radius around the room entrance — NPCs inside this radius are candidates for repositioning.")]
    public float npcProximityRadius = 8f;

    [Tooltip("Seconds the player must stay inside with all doors closed before NPCs are repositioned.")]
    public float shuffleDelay = 5f;

    [Tooltip("Cooldown after a shuffle before it can trigger again.")]
    public float shuffleCooldown = 10f;

    // The SafeRoomDoor components belonging to this room's entrance.
    // All must be closed for the shuffle to trigger.
    private List<SafeRoomDoor> roomDoors = new List<SafeRoomDoor>();
    private int levelIndex;

    private Transform player;
    private DungeonNavMeshSetup dungeonNavMeshSetup;

    private float timer = 0f;
    private float cooldownTimer = 0f;

    // Tracks whether this room is currently registering the player as protected
    // (inside + all doors closed), so we can correctly unregister when conditions
    // stop being met.
    private bool _isProtecting = false;

    // Tracks whether the player is physically inside this room regardless of door
    // state — used so PacerNPC can break chase as soon as the player steps in.
    private bool _isInRoom = false;

    /// <summary>
    /// Called by SpawnRoomSetup / SafeRoomSetup immediately after instantiation.
    /// </summary>
    public void Initialise(Vector3 center, int level, List<SafeRoomDoor> doors)
    {
        roomCenter = center;
        levelIndex = level;
        roomDoors  = doors ?? new List<SafeRoomDoor>();
    }

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        dungeonNavMeshSetup = FindObjectOfType<DungeonNavMeshSetup>();
    }

    private void Update()
    {
        // Nothing to do if there are no doors — can't guarantee the player is enclosed.
        if (player == null || roomDoors.Count == 0) return;

        bool playerInside   = IsPlayerInside();
        bool playerEnclosed = playerInside && AreAllDoorsClosed();

        // Track whether the player is physically in the room (any door state).
        // PacerNPC uses IsPlayerInRoom to break chase before the doors are closed.
        if (playerInside && !_isInRoom)
        {
            PlayerSafeZone.RegisterRoomEntry();
            _isInRoom = true;
        }
        else if (!playerInside && _isInRoom)
        {
            PlayerSafeZone.UnregisterRoomEntry();
            _isInRoom = false;
        }

        // Register / unregister full NPC-vision protection (inside + doors closed).
        if (playerEnclosed && !_isProtecting)
        {
            PlayerSafeZone.RegisterProtection();
            _isProtecting = true;
        }
        else if (!playerEnclosed && _isProtecting)
        {
            PlayerSafeZone.UnregisterProtection();
            _isProtecting = false;
        }

        // Run down the cooldown, then resume normal checks.
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        if (playerEnclosed)
        {
            timer += Time.deltaTime;

            if (timer >= shuffleDelay)
            {
                TriggerShuffle();
                timer = 0f;
                cooldownTimer = shuffleCooldown;
            }
        }
        else
        {
            // Reset the countdown if the player steps out or a door is opened.
            timer = 0f;
        }
    }

    private void OnDestroy() => ReleaseRegistrations();

    // Release on disable too — when the player moves between levels, the old
    // level's parent is SetActive(false) and this Update() stops running.
    // Without this, stale counters leave IsPlayerInRoom / IsPlayerProtected
    // permanently true, causing Pacers on the new level to never chase.
    private void OnDisable() => ReleaseRegistrations();

    private void ReleaseRegistrations()
    {
        if (_isProtecting)
        {
            PlayerSafeZone.UnregisterProtection();
            _isProtecting = false;
        }
        if (_isInRoom)
        {
            PlayerSafeZone.UnregisterRoomEntry();
            _isInRoom = false;
        }
        // Reset the shuffle timer so re-enabling the level doesn't instantly fire it.
        timer = 0f;
    }

    private bool IsPlayerInside()
    {
        // Y-axis distance is intentionally included so the check works across
        // different floor levels in the same vertical column.
        return Vector3.Distance(player.position, roomCenter) <= playerInsideRadius;
    }

    private bool AreAllDoorsClosed()
    {
        foreach (SafeRoomDoor door in roomDoors)
        {
            if (door == null) continue;   // door may have been destroyed
            if (door.IsOpen) return false;
        }
        return true;
    }

    private void TriggerShuffle()
    {
        if (dungeonNavMeshSetup == null) return;

        NPCSpawnManager manager = dungeonNavMeshSetup.GetSpawnManagerForLevel(levelIndex);
        if (manager == null)
        {
            Debug.LogWarning($"[RoomNPCShuffle] No spawn manager found for level {levelIndex}.");
            return;
        }

        manager.ShuffleNearbyNPCs(roomCenter, npcProximityRadius);
    }
}
