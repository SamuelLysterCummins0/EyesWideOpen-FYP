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
        public Keypad  keypad;
        public List<GameObject> numbers = new List<GameObject>();
    }

    private Dictionary<int, LevelData> levelStates = new Dictionary<int, LevelData>();
    private int currentInitLevel = 0; // set at the top of InitializeForLevel

    // ── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ── Called by ProceduralDungeonGenerator ─────────────────────────────────
    /// <summary>
    /// Entry point called by the dungeon generator after a level finishes building.
    /// Clears any previous state, generates a new code, and spawns numbers.
    /// </summary>
    public void InitializeForLevel(ProceduralDungeonGenerator generator, int levelIndex, int startX, int startZ)
    {
        currentInitLevel = levelIndex;

        // Only clear numbers that belong to THIS level — leaves other levels intact.
        ClearLevelNumbers(levelIndex);

        // Create (or overwrite) state for this level.
        LevelData data = new LevelData();
        levelStates[levelIndex] = data;

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
        data.keypad = FindObjectOfType<Keypad>();
        if (data.keypad != null)
        {
            data.keypad.SetCode(combinedCode);
            data.keypad.SetCodesCollected(false);
        }
        else
        {
            Debug.LogWarning("[CodeNumberManager] No Keypad found in scene.");
        }

        // Find all valid, reachable tile positions.
        List<Vector2Int> reachable = generator.GetReachableTilePositions(startX, startZ);

        if (reachable.Count < 4)
        {
            Debug.LogWarning("[CodeNumberManager] Not enough reachable tiles to place 4 numbers.");
            return;
        }

        ShuffleList(reachable);

        List<Vector3> chosenWorldPositions = new List<Vector3>();
        List<Vector2Int> chosenGridPositions = new List<Vector2Int>();

        foreach (Vector2Int gridPos in reachable)
        {
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

            if (chosenWorldPositions.Count >= 4) break;
        }

        if (chosenWorldPositions.Count < 4)
        {
            Debug.LogWarning($"[CodeNumberManager] Could only place {chosenWorldPositions.Count}/4 numbers. Try reducing minSpreadDistance.");
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
    /// Called by a CodeNumber when the player successfully gazes at it.
    /// levelIndex tells us which level's state and keypad to update.
    /// </summary>
    public void OnDigitCollected(int levelIndex, int orderIndex, int digit)
    {
        if (!levelStates.TryGetValue(levelIndex, out LevelData data)) return;
        if (data.collected[orderIndex]) return; // Already collected.

        data.collected[orderIndex] = true;
        data.collectedCount++;

        Debug.Log($"[CodeNumberManager] Level {levelIndex} digit {orderIndex + 1}/4 collected: {digit}");

        if (hud != null) hud.UpdateSlot(orderIndex, digit, data.collectedCount);

        if (data.collectedCount >= 4)
        {
            if (data.keypad != null) data.keypad.SetCodesCollected(true);
            if (hud != null) hud.ShowAllCollectedMessage();
            Debug.Log($"[CodeNumberManager] Level {levelIndex}: all 4 digits collected. Keypad unlocked!");
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

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
