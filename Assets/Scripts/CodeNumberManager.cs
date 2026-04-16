using System.Collections.Generic;
using UnityEngine;
using NavKeypad;

/// <summary>
/// Manages the 4-digit code collection system.
/// Generates a random code each level, spawns CodeNumber objects on valid interior walls,
/// and syncs the final code to the Keypad once all digits are collected.
///
/// Add this as a component in your scene (one instance). It gets called by
/// ProceduralDungeonGenerator after each level is built.
/// </summary>
public class CodeNumberManager : MonoBehaviour
{
    public static CodeNumberManager Instance { get; private set; }

    [Header("Spawning")]
    [Tooltip("The CodeNumber prefab to spawn on walls.")]
    [SerializeField] private GameObject codeNumberPrefab;

    [Tooltip("Minimum world-space distance between any two spawned numbers.")]
    [SerializeField] private float minSpreadDistance = 8f;

    [Tooltip("How far from the tile centre to search for a wall surface via raycast.")]
    [SerializeField] private float wallRaycastDistance = 2.5f;

    [Header("References")]
    [SerializeField] private CodeNumberHUD hud;

    // ── Per-level state ───────────────────────────────────────────────────────
    // Keeps digits, collection status, keypad ref, and spawned objects separate
    // per level so generating level 1 doesn't destroy level 0's numbers.
    private class LevelData
    {
        public int[]   digits    = new int[4];
        public bool[]  collected = new bool[4];
        public int     collectedCount = 0;
        public int     sirenTriggerSlot = -1; // Slot (0-2) whose collection triggers the siren. -1 = none.
        public bool    sirenTriggered   = false; // Guards against triggering more than once per run.
        public Keypad  keypad;
        public List<GameObject> numbers = new List<GameObject>();
    }

    private Dictionary<int, LevelData> levelStates = new Dictionary<int, LevelData>();
    private int currentInitLevel = 0; // set at the top of InitializeForLevel

    // Tiles registered by HiddenRoomSetup that should be skipped during wall-number placement
    private HashSet<Vector2Int> excludedTiles = new HashSet<Vector2Int>();

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CodeNumberManager] Duplicate detected and destroyed. Only one CodeNumberManager should be in the scene.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log("[CodeNumberManager] Singleton instance set.");
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;

        // Application.isPlaying stays true during play-mode teardown, so we can't
        // use it alone to distinguish "mid-session destroy" from "normal exit".
        // isQuitting is set in OnApplicationQuit (called before OnDestroy on exit).
        if (Application.isPlaying && !isQuitting)
            Debug.LogError("[CodeNumberManager] DESTROYED DURING PLAY — Instance will become null! Check if its parent GameObject is being destroyed mid-session.");
    }

    private bool isQuitting = false;
    private void OnApplicationQuit() { isQuitting = true; }

    // ── Public Query API ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the world positions of every spawned CodeNumber object across all levels.
    /// Used by LockerSetup to avoid placing lockers on tiles that already have digits on them.
    /// </summary>
    public List<Vector3> GetSpawnedPositions()
    {
        List<Vector3> positions = new List<Vector3>();
        foreach (var kvp in levelStates)
        {
            foreach (GameObject obj in kvp.Value.numbers)
            {
                if (obj != null)
                    positions.Add(obj.transform.position);
            }
        }
        return positions;
    }

    // ── Called by ProceduralDungeonGenerator ─────────────────────────────────
    /// <summary>
    /// Entry point called by the dungeon generator after a level finishes building.
    /// Clears any previous state, generates a new code, and spawns numbers.
    /// </summary>
    public void InitializeForLevel(ProceduralDungeonGenerator generator, int levelIndex, int startX, int startZ, int wallNumberCount = 2)
    {
        currentInitLevel = levelIndex;

        // Only clear numbers that belong to THIS level — leaves other levels intact.
        ClearLevelNumbers(levelIndex);

        // Create (or overwrite) state for this level.
        LevelData data = new LevelData();
        levelStates[levelIndex] = data;

        // Pick one of slots 0-2 to trigger the siren when collected.
        // Slot 3 (computer terminal) is never chosen.
        data.sirenTriggerSlot = Random.Range(0, 3);
        Debug.Log($"[CodeNumberManager] Level {levelIndex}: siren trigger slot = {data.sirenTriggerSlot}");

        // Generate 4 random digits (0-9, repeats allowed).
        for (int i = 0; i < 4; i++)
            data.digits[i] = Random.Range(0, 10);

        int combinedCode = (data.digits[0] * 1000) + (data.digits[1] * 100)
                         + (data.digits[2] * 10)   + data.digits[3];

        Debug.Log($"[CodeNumberManager] Level {levelIndex} code: {combinedCode} ({data.digits[0]}{data.digits[1]}{data.digits[2]}{data.digits[3]})");

        // Auto-find HUD.
        if (hud == null) hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud == null) Debug.LogWarning("[CodeNumberManager] CodeNumberHUD not found in scene.");

        // Push code to keypad.
        // Multiple levels are generated simultaneously, so find all keypads and pick the one
        // closest in Y to this level's floor height — but only if it falls within half a level
        // height. Final levels have no keypad (no stairs), so they get none rather than stealing
        // the previous level's keypad and overwriting its code.
        float expectedY   = levelIndex * -generator.LevelHeight;
        float maxDist     = generator.LevelHeight * 0.5f;
        Keypad[] allKeypads = FindObjectsOfType<Keypad>();
        data.keypad = null;
        float bestDist = float.MaxValue;
        foreach (Keypad kp in allKeypads)
        {
            float dist = Mathf.Abs(kp.transform.position.y - expectedY);
            if (dist < bestDist) { bestDist = dist; data.keypad = kp; }
        }

        // Reject the match if it is too far away — this level has no keypad of its own
        if (bestDist > maxDist) data.keypad = null;

        if (data.keypad != null)
        {
            data.keypad.SetCode(combinedCode);
            data.keypad.SetCodesCollected(false);
            Debug.Log($"[CodeNumberManager] Level {levelIndex}: keypad at Y={data.keypad.transform.position.y:F1} (expected {expectedY:F1}), code={combinedCode}.");
        }
        else
        {
            Debug.Log($"[CodeNumberManager] Level {levelIndex}: no keypad within {maxDist:F1} units of Y={expectedY:F1} — level has no exit keypad.");
        }

        // Find all valid, reachable tile positions.
        List<Vector2Int> reachable = generator.GetReachableTilePositions(startX, startZ);

        if (reachable.Count < 2)
        {
            Debug.LogWarning("[CodeNumberManager] Not enough reachable tiles to place wall numbers.");
            return;
        }

        ShuffleList(reachable);

        List<Vector3> chosenWorldPositions = new List<Vector3>();
        List<Vector2Int> chosenGridPositions = new List<Vector2Int>();

        foreach (Vector2Int gridPos in reachable)
        {
            // Skip tiles reserved for the hidden room by HiddenRoomSetup
            if (excludedTiles.Contains(gridPos)) continue;

            GameObject tile = generator.GetPlacedTile(gridPos.x, gridPos.y);
            ProceduralDungeonGenerator.TileConfig config = generator.GetTileConfig(gridPos.x, gridPos.y);

            if (tile == null || config == null) continue;

            Vector3? wallPos = FindValidWallSurface(tile, config, generator.TileSize, out Vector3 wallNormal);
            if (wallPos == null) continue;

            bool tooClose = false;
            foreach (Vector3 existing in chosenWorldPositions)
            {
                if (Vector3.Distance(wallPos.Value, existing) < minSpreadDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            chosenWorldPositions.Add(wallPos.Value);
            chosenGridPositions.Add(gridPos);

            int digitIndex = chosenGridPositions.Count - 1;

            // Capture levelIndex for the closure so the right level's state is updated.
            int capturedLevel = levelIndex;
            SpawnCodeNumber(wallPos.Value, wallNormal, data.digits[digitIndex], digitIndex, capturedLevel);

            // Only 2 wall numbers — slot 2 comes from the hidden room, slot 3 from the computer
            if (chosenWorldPositions.Count >= wallNumberCount) break;
        }

        if (chosenWorldPositions.Count < wallNumberCount)
        {
            Debug.LogWarning($"[CodeNumberManager] Could only place {chosenWorldPositions.Count}/{wallNumberCount} wall numbers. Try reducing minSpreadDistance.");
        }

        if (hud != null) hud.ResetDisplay();
    }

    // ── Spawning ─────────────────────────────────────────────────────────────

    private void SpawnCodeNumber(Vector3 position, Vector3 wallNormal, int digit, int orderIndex, int levelIndex)
    {
        if (codeNumberPrefab == null)
        {
            Debug.LogError("[CodeNumberManager] codeNumberPrefab is not assigned!");
            return;
        }

        Quaternion rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);

        GameObject obj = Instantiate(codeNumberPrefab, position, rotation);
        obj.name = $"CodeNumber_L{levelIndex}_{orderIndex}_{digit}";

        // Track the object under this level so we can clean it up independently.
        if (levelStates.TryGetValue(levelIndex, out LevelData data))
            data.numbers.Add(obj);

        CodeNumber codeNum = obj.GetComponent<CodeNumber>();
        if (codeNum != null)
        {
            // Closure captures levelIndex so the callback updates the correct level's state.
            codeNum.Initialise(digit, orderIndex, (oi, d) => OnDigitCollected(levelIndex, oi, d));
        }
        else
        {
            Debug.LogError("[CodeNumberManager] codeNumberPrefab is missing CodeNumber component!");
        }
    }

    /// <summary>
    /// Scans the four faces of a tile to find an interior wall surface suitable for placement.
    /// Returns the world position (at eye height) on success, null if no valid face found.
    /// </summary>
    private Vector3? FindValidWallSurface(GameObject tile, ProceduralDungeonGenerator.TileConfig config,
                                          float tileSize, out Vector3 outNormal)
    {
        outNormal = Vector3.forward;
        float halfSize = tileSize * 0.5f;

        // Direction pairs: (offset from centre to wall face, inward normal pointing into room)
        // North wall face is on the +Z side of the tile; inward normal is -Z (into room from north wall)
        (Vector3 offset, Vector3 inwardNormal, ProceduralDungeonGenerator.EdgeType edge)[] faces =
        {
            (Vector3.forward  * halfSize, Vector3.back,  config.north),
            (Vector3.back     * halfSize, Vector3.forward, config.south),
            (Vector3.right    * halfSize, Vector3.left,  config.east),
            (Vector3.left     * halfSize, Vector3.right, config.west),
        };

        // Shuffle faces so we don't always pick the same side.
        for (int i = faces.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = faces[i]; faces[i] = faces[j]; faces[j] = tmp;
        }

        foreach (var (offset, inwardNormal, edge) in faces)
        {
            // We want Wall edges — those are the solid interior walls we can paint on.
            if (edge != ProceduralDungeonGenerator.EdgeType.Wall) continue;

            Vector3 wallFaceCenter = tile.transform.position + offset;
            Vector3 eyeHeightPos = new Vector3(wallFaceCenter.x, tile.transform.position.y + 1.5f, wallFaceCenter.z);

            // Cast a ray from tile centre toward the wall to confirm geometry is actually there.
            Vector3 rayOrigin = tile.transform.position + Vector3.up * 1.5f;
            Vector3 rayDir = offset.normalized;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, wallRaycastDistance))
            {
                // Pull the spawn point off the wall surface far enough that the quad
                // and its BoxCollider don't clip into the geometry (0.12 = ~12cm clearance).
                Vector3 spawnPos = hit.point + inwardNormal * 0.12f;
                spawnPos.y = tile.transform.position.y + 1.5f;
                outNormal = inwardNormal;
                return spawnPos;
            }
            else
            {
                // Fallback: trust the config and place at the calculated position.
                outNormal = inwardNormal;
                return eyeHeightPos + inwardNormal * 0.12f;
            }
        }

        return null; // No valid wall face on this tile.
    }

    // ── Collection Callback ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the pre-generated digit for a given level and slot index (0–3).
    /// Digit 3 is not spawned on a wall — it is revealed by the computer terminal maze.
    /// </summary>
    public int GetDigit(int levelIndex, int orderIndex)
    {
        if (!levelStates.TryGetValue(levelIndex, out LevelData data)) return -1;
        if (orderIndex < 0 || orderIndex >= data.digits.Length) return -1;
        return data.digits[orderIndex];
    }

    /// <summary>
    /// Called by a CodeNumber (wall) or by MazeMinigame (computer terminal) when a digit is collected.
    /// levelIndex tells us which level's state and keypad to update.
    /// </summary>
    public void OnDigitCollected(int levelIndex, int orderIndex, int digit)
    {
        if (!levelStates.TryGetValue(levelIndex, out LevelData data))
        {
            // Level state missing — create a minimal one so the HUD can still be updated.
            // This is a fallback for cases where InitializeForLevel ran on a different instance.
            Debug.LogWarning($"[CodeNumberManager] OnDigitCollected: no level state for level {levelIndex} — creating fallback state.");
            data = new LevelData();
            if (orderIndex >= 0 && orderIndex < data.digits.Length)
                data.digits[orderIndex] = digit;
            levelStates[levelIndex] = data;
        }
        if (data.collected[orderIndex]) return; // Already collected.

        data.collected[orderIndex] = true;
        data.collectedCount++;

        Debug.Log($"[CodeNumberManager] Level {levelIndex}: collected slot {orderIndex + 1} (value={digit}), total collected = {data.collectedCount}/4");

        // Re-find HUD every time in case it wasn't assigned during InitializeForLevel
        // (e.g. when called from MazeMinigame after the level was already set up)
        if (hud == null) hud = FindObjectOfType<CodeNumberHUD>(true);

        if (hud != null)
        {
            hud.UpdateSlot(orderIndex, digit, data.collectedCount);
        }
        else
        {
            Debug.LogWarning("[CodeNumberManager] CodeNumberHUD not found — digit collected but HUD not updated.");
        }

        // Trigger siren phase if this is the designated trigger slot for this level.
        if (!data.sirenTriggered && orderIndex == data.sirenTriggerSlot && SirenPhaseManager.Instance != null)
        {
            data.sirenTriggered = true;
            SirenPhaseManager.Instance.TriggerOnCodeCollected(levelIndex);
            Debug.Log($"[CodeNumberManager] Level {levelIndex}: siren triggered by designated slot {orderIndex}.");
        }

        // Mandatory fallback: if 3 digits have been collected and the siren still hasn't
        // fired (e.g. the designated slot's number was never spawned), trigger it now so
        // the siren phase is guaranteed to happen before the level is completed.
        if (!data.sirenTriggered && data.collectedCount >= 3
            && SirenPhaseManager.Instance != null)
        {
            data.sirenTriggered = true;
            SirenPhaseManager.Instance.TriggerOnCodeCollected(levelIndex);
            Debug.Log($"[CodeNumberManager] Level {levelIndex}: siren triggered by mandatory fallback (3 digits collected).");
        }

        if (data.collectedCount >= 4)
        {
            if (data.keypad != null) data.keypad.SetCodesCollected(true);
            if (hud != null) hud.ShowAllCollectedMessage();
            Debug.Log($"[CodeNumberManager] Level {levelIndex}: all 4 digits collected. Keypad unlocked!");
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameManager when the player respawns.
    /// Re-enables all collected wall numbers for the current level and clears
    /// the collection state so the player must gather them again.
    /// The level layout, digit values, and number positions are preserved.
    /// </summary>
    public void ResetCollectionForLevel(int levelIndex)
    {
        Debug.Log($"[CodeNumberManager] ResetCollectionForLevel({levelIndex}) called.");

        if (!levelStates.TryGetValue(levelIndex, out LevelData data))
        {
            Debug.LogWarning($"[CodeNumberManager] ResetCollectionForLevel: no level state found for level {levelIndex}. Was InitializeForLevel called?");
            return;
        }

        Debug.Log($"[CodeNumberManager] Level {levelIndex} has {data.numbers.Count} number objects tracked.");

        // Re-enable every physical CodeNumber in this level.
        int resetCount = 0;
        int nullCount  = 0;
        foreach (GameObject obj in data.numbers)
        {
            if (obj == null)
            {
                nullCount++;
                continue;
            }
            CodeNumber cn = obj.GetComponent<CodeNumber>();
            if (cn != null)
            {
                cn.ResetForRespawn();
                resetCount++;
            }
            else
            {
                obj.SetActive(true);
                resetCount++;
            }
        }

        if (nullCount > 0)
            Debug.LogWarning($"[CodeNumberManager] {nullCount} number object(s) were null (destroyed). " +
                             "This means they were created with old code that called Destroy() instead of SetActive(false). " +
                             "EXIT AND RE-ENTER PLAY MODE to fix — the new code uses SetActive so references persist.");
        else
            Debug.Log($"[CodeNumberManager] Reset {resetCount} CodeNumber objects on level {levelIndex}.");

        // Clear collection tracking (digits 0-3).
        for (int i = 0; i < 4; i++)
            data.collected[i] = false;
        data.collectedCount  = 0;
        data.sirenTriggered  = false;

        // Re-randomise siren trigger slot for the new round (never slot 3 = computer).
        data.sirenTriggerSlot = Random.Range(0, 3);
        Debug.Log($"[CodeNumberManager] Level {levelIndex}: siren trigger slot re-randomised to {data.sirenTriggerSlot}");

        // Lock the keypad again.
        if (data.keypad != null)
            data.keypad.SetCodesCollected(false);

        // Reset HUD to empty.
        if (hud == null) hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud != null)
        {
            hud.ResetDisplay();
            Debug.Log("[CodeNumberManager] HUD reset.");
        }
        else
        {
            Debug.LogWarning("[CodeNumberManager] CodeNumberHUD not found — HUD not reset!");
        }
    }

    // Called by GameManager.SetCurrentLevel() when the player descends to a new level.
    // Resets the HUD to reflect the new level's collection state (empty on first visit,
    // already-collected slots filled in if revisiting).
    public void ActivateLevel(int levelIndex)
    {
        if (hud == null) hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud == null) return;

        hud.ResetDisplay();

        if (!levelStates.TryGetValue(levelIndex, out LevelData data)) return;

        // Re-fill any slots the player already collected on this level (e.g. after respawn).
        for (int i = 0; i < 4; i++)
        {
            if (data.collected[i])
                hud.UpdateSlot(i, data.digits[i], data.collectedCount);
        }
    }

    // Destroys only the numbers belonging to one specific level.
    private void ClearLevelNumbers(int levelIndex)
    {
        if (!levelStates.TryGetValue(levelIndex, out LevelData data)) return;

        foreach (GameObject obj in data.numbers)
        {
            if (obj == null) continue;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
        data.numbers.Clear();
        levelStates.Remove(levelIndex);
    }

    // Called by ProceduralDungeonGenerator.ClearDungeon() — wipes everything.
    public void ClearAll()
    {
        foreach (var kvp in levelStates)
        {
            foreach (GameObject obj in kvp.Value.numbers)
            {
                if (obj == null) continue;
                if (Application.isPlaying) Destroy(obj);
                else DestroyImmediate(obj);
            }
        }
        levelStates.Clear();
    }

    // ── Hidden room / excluded tile integration ───────────────────────────────

    /// <summary>
    /// Called by HiddenRoomSetup to place the slot-2 code number on a Wall face
    /// inside the hidden room tile.
    /// Returns the spawned GameObject so HiddenRoomSetup can track it for cleanup.
    /// NOTE: Must be called AFTER InitializeForLevel so level state exists.
    /// </summary>
    public GameObject SpawnHiddenRoomNumber(
        GameObject tile,
        ProceduralDungeonGenerator.TileConfig config,
        float tileSize,
        int levelIndex)
    {
        if (!levelStates.TryGetValue(levelIndex, out LevelData data))
        {
            Debug.LogWarning("[CodeNumberManager] SpawnHiddenRoomNumber: level state not found — was InitializeForLevel called first?");
            return null;
        }

        Vector3? wallPos = FindValidWallSurface(tile, config, tileSize, out Vector3 wallNormal);
        if (wallPos == null)
        {
            // This means the tile has NO Wall-type edges at all — FindHiddenRoomTile should have prevented this
            Debug.LogError($"[CodeNumberManager] SpawnHiddenRoomNumber(L{levelIndex}): FindValidWallSurface returned null. " +
                           $"Tile edges — N:{config.north} S:{config.south} E:{config.east} W:{config.west}. " +
                           "Ensure the hidden room tile has at least one Wall edge.");
            return null;
        }

        Debug.Log($"[CodeNumberManager] SpawnHiddenRoomNumber(L{levelIndex}): wall surface found at {wallPos.Value}, normal {wallNormal}.");

        int digitIndex = 2; // slot 2 is always the hidden-room number
        int digit      = data.digits[digitIndex];

        if (codeNumberPrefab == null)
        {
            Debug.LogError("[CodeNumberManager] codeNumberPrefab is not assigned!");
            return null;
        }

        Quaternion rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);
        GameObject obj = Instantiate(codeNumberPrefab, wallPos.Value, rotation);
        obj.name = $"CodeNumber_L{levelIndex}_Hidden_{digit}";

        data.numbers.Add(obj);

        CodeNumber codeNum = obj.GetComponent<CodeNumber>();
        if (codeNum != null)
            codeNum.Initialise(digit, digitIndex, (oi, d) => OnDigitCollected(levelIndex, oi, d));
        else
            Debug.LogError("[CodeNumberManager] codeNumberPrefab is missing CodeNumber component!");

        return obj;
    }

    /// <summary>
    /// Registers a tile grid position that should be skipped during wall-number placement.
    /// Called by HiddenRoomSetup before InitializeForLevel runs.
    /// </summary>
    public void ExcludeTile(Vector2Int gridPos)
    {
        excludedTiles.Add(gridPos);
    }

    /// <summary>
    /// Clears the excluded-tile set — called by HiddenRoomSetup.ClearAll() before regeneration.
    /// </summary>
    public void ClearExcludedTiles()
    {
        excludedTiles.Clear();
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}
