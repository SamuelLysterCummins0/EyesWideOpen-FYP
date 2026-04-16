using UnityEngine;

/// <summary>
/// Positions the intro room prefab above the Level 0 spawn room each time the
/// dungeon is procedurally generated.
///
/// PLACEMENT LOGIC
///   The intro room prefab contains a child Transform named "DropPoint" at the
///   end of its hallway — the hole through which the player falls into the dungeon.
///   This script moves the prefab root so that the DropPoint's XZ position aligns
///   exactly with Level 0's spawn room, and sits HeightAboveLevel0 units above it.
///
/// SETUP (scene object — NOT inside the prefab)
///   1. Create an empty GameObject in the scene (e.g. "IntroRoomSetup").
///   2. Attach this script.
///   3. Assign IntroRoomPrefab in the Inspector.
///   4. Set DropPointChildName to match the exact name of the child transform in
///      the prefab that marks where the player falls through (default "DropPoint").
///   5. Tune HeightAboveLevel0 so the intro room floor sits a comfortable distance
///      above the dungeon ceiling on Level 0 (default 8 units).
///
/// CALLED BY
///   ProceduralDungeonGenerator — after all levels have been generated, before
///   PlacePlayerAtSpawnRoom().
/// </summary>
public class IntroRoomSetup : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("The intro room prefab (Security Entry Suite).")]
    public GameObject IntroRoomPrefab;

    [Header("Placement")]
    [Tooltip("Name of the child transform in the prefab that marks the drop-hole " +
             "position (end of hallway). Must match the prefab hierarchy exactly.")]
    public string DropPointChildName = "DropPoint";

    [Tooltip("How many units above the Level 0 floor the intro room sits. " +
             "Increase if the intro room overlaps the dungeon ceiling.")]
    public float HeightAboveLevel0 = 8f;

    [Tooltip("Name of the child transform in the prefab where the player spawns " +
             "at game start (inside the security entry room, facing the tablet). " +
             "Must match the prefab hierarchy exactly.")]
    public string PlayerStartChildName = "PlayerStart";

    // ── Singleton ─────────────────────────────────────────────────────────────
    public static IntroRoomSetup Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    // ── Runtime state ──────────────────────────────────────────────────────────
    private GameObject spawnedRoom;

    // Cached world position of PlayerStart after the room is placed.
    private Vector3  playerStartWorld;
    private bool     hasPlayerStart;

    // ── Entry point ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ProceduralDungeonGenerator after all levels are generated.
    /// Destroys any previous instance and spawns a fresh one aligned to the new layout.
    /// </summary>
    public void PlaceIntroRoom(SpawnRoomSetup spawnRoomSetup)
    {
        if (IntroRoomPrefab == null)
        {
            Debug.LogError("[IntroRoomSetup] IntroRoomPrefab is not assigned!");
            return;
        }

        if (spawnRoomSetup == null || !spawnRoomSetup.HasSpawnPoint(0))
        {
            Debug.LogWarning("[IntroRoomSetup] Level 0 spawn point not available yet.");
            return;
        }

        // Destroy old instance (on dungeon regeneration)
        if (spawnedRoom != null)
            Destroy(spawnedRoom);

        Vector3 spawnRoomWorldPos = spawnRoomSetup.GetSpawnPoint(0);

        // Measure where DropPoint sits relative to the prefab root by briefly
        // instantiating at the origin, reading the local offset, then destroying.
        Vector3 dropLocalOffset = MeasureDropPointOffset();

        // The room root must be positioned so that:
        //   roomRoot + dropLocalOffset  =  spawnRoomWorldPos + (0, HeightAboveLevel0, 0)
        Vector3 targetDropWorld = new Vector3(
            spawnRoomWorldPos.x,
            spawnRoomWorldPos.y + HeightAboveLevel0,
            spawnRoomWorldPos.z);

        Vector3 roomRootPos = targetDropWorld - dropLocalOffset;

        spawnedRoom      = Instantiate(IntroRoomPrefab, roomRootPos, Quaternion.identity);
        spawnedRoom.name = "IntroRoom";

        // Cache the PlayerStart world position so GameManager can teleport here.
        Transform playerStartTf = spawnedRoom.transform.Find(PlayerStartChildName);
        if (playerStartTf != null)
        {
            playerStartWorld = playerStartTf.position;
            hasPlayerStart   = true;
        }
        else
        {
            hasPlayerStart = false;
            Debug.LogWarning($"[IntroRoomSetup] Child '{PlayerStartChildName}' not found in prefab. " +
                             $"Player will spawn at the dungeon spawn room instead. " +
                             $"Add an empty child named '{PlayerStartChildName}' to the prefab.");
        }

        Debug.Log($"[IntroRoomSetup] Intro room placed. Root={roomRootPos}, " +
                  $"DropPoint aligned to ({spawnRoomWorldPos.x:F1}, _, {spawnRoomWorldPos.z:F1}) " +
                  $"at Y={targetDropWorld.y:F1}. PlayerStart={(hasPlayerStart ? playerStartWorld.ToString() : "not found")}");
    }

    // ── Public API — used by GameManager ──────────────────────────────────────

    /// <summary>True after PlaceIntroRoom() succeeds and the PlayerStart child was found.</summary>
    public bool HasPlayerStart() => hasPlayerStart && spawnedRoom != null;

    /// <summary>World-space position the player should spawn at game start.</summary>
    public Vector3 GetPlayerStartPosition() => playerStartWorld;

    // ── Cleanup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Disables the intro room for performance once the player has entered the dungeon.
    /// Called by SpawnRoomCheckpoint when Level 0 is activated.
    /// </summary>
    public void DisableIntroRoom()
    {
        if (spawnedRoom != null)
            spawnedRoom.SetActive(false);
    }

    /// <summary>Destroys the spawned room. Called by ProceduralDungeonGenerator.ClearDungeon().</summary>
    public void ClearRoom()
    {
        if (spawnedRoom != null)
            Destroy(spawnedRoom);

        spawnedRoom    = null;
        hasPlayerStart = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instantiate the prefab at the world origin for one frame to read the
    /// DropPoint child's world position (= its local offset when root is at origin).
    /// Destroys the temporary instance immediately.
    /// </summary>
    private Vector3 MeasureDropPointOffset()
    {
        GameObject temp = Instantiate(IntroRoomPrefab, Vector3.zero, Quaternion.identity);
        Transform  drop = temp.transform.Find(DropPointChildName);

        Vector3 offset = Vector3.zero;
        if (drop != null)
        {
            offset = drop.position; // world pos when root is at origin == local offset
        }
        else
        {
            Debug.LogWarning($"[IntroRoomSetup] Could not find child '{DropPointChildName}' " +
                             $"in prefab. DropPoint offset treated as (0,0,0). " +
                             $"Make sure the prefab has a child named '{DropPointChildName}'.");
        }

        DestroyImmediate(temp);
        return offset;
    }
}
