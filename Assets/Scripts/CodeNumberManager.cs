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

    // ── Runtime State ────────────────────────────────────────────────────────
    private int[] generatedDigits = new int[4]; // The four individual digits
    private bool[] digitsCollected = new bool[4];
    private int collectedCount = 0;
    private Keypad currentKeypad;

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
        ClearPreviousNumbers();
        ResetState();

        // Generate 4 random digits (0-9, repeats allowed).
        for (int i = 0; i < 4; i++)
            generatedDigits[i] = Random.Range(0, 10);

        int combinedCode = (generatedDigits[0] * 1000) + (generatedDigits[1] * 100)
                         + (generatedDigits[2] * 10) + generatedDigits[3];

        Debug.Log($"[CodeNumberManager] Level {levelIndex} code: {combinedCode} ({generatedDigits[0]}{generatedDigits[1]}{generatedDigits[2]}{generatedDigits[3]})");

        // Auto-find HUD — pass true so inactive GameObjects are included.
        // The panel may start disabled in the scene; without the flag FindObjectOfType skips it.
        if (hud == null) hud = FindObjectOfType<CodeNumberHUD>(true);
        if (hud == null) Debug.LogWarning("[CodeNumberManager] CodeNumberHUD not found in scene.");

        // Push code to keypad - player still can't use it until all digits collected.
        currentKeypad = FindObjectOfType<Keypad>();
        if (currentKeypad != null)
        {
            currentKeypad.SetCode(combinedCode);
            currentKeypad.SetCodesCollected(false);
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

        // Shuffle the list so we pick spread-out positions randomly.
        ShuffleList(reachable);

        // Pick 4 positions that are spread far enough apart from each other.
        List<Vector3> chosenWorldPositions = new List<Vector3>();
        List<Vector2Int> chosenGridPositions = new List<Vector2Int>();

        foreach (Vector2Int gridPos in reachable)
        {
            GameObject tile = generator.GetPlacedTile(gridPos.x, gridPos.y);
            ProceduralDungeonGenerator.TileConfig config = generator.GetTileConfig(gridPos.x, gridPos.y);

            if (tile == null || config == null) continue;

            Vector3? wallPos = FindValidWallSurface(tile, config, generator.TileSize, out Vector3 wallNormal);
            if (wallPos == null) continue;

            // Enforce minimum spread between numbers.
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

            // Spawn the CodeNumber object.
            SpawnCodeNumber(wallPos.Value, wallNormal, generatedDigits[chosenGridPositions.Count - 1], chosenGridPositions.Count - 1);

            if (chosenWorldPositions.Count >= 4) break;
        }

        if (chosenWorldPositions.Count < 4)
        {
            Debug.LogWarning($"[CodeNumberManager] Could only place {chosenWorldPositions.Count}/4 numbers. Try reducing minSpreadDistance.");
        }

        // Initialise HUD display (all slots empty).
        if (hud != null) hud.ResetDisplay();
    }

    // ── Spawning ─────────────────────────────────────────────────────────────

    private void SpawnCodeNumber(Vector3 position, Vector3 wallNormal, int digit, int orderIndex)
    {
        if (codeNumberPrefab == null)
        {
            Debug.LogError("[CodeNumberManager] codeNumberPrefab is not assigned!");
            return;
        }

        // Orient the number to face INTO the room (away from wall).
        // Negate wallNormal: LookRotation points the object's +Z along the given direction,
        // but the Quad prefab child already faces +Z, so we need the root to face AWAY from
        // the wall (i.e. toward the player) — which means pointing -wallNormal outward.
        Quaternion rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);

        GameObject obj = Instantiate(codeNumberPrefab, position, rotation);
        obj.name = $"CodeNumber_{orderIndex}_{digit}";

        CodeNumber codeNum = obj.GetComponent<CodeNumber>();
        if (codeNum != null)
        {
            codeNum.Initialise(digit, orderIndex, OnDigitCollected);
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
    /// </summary>
    public void OnDigitCollected(int orderIndex, int digit)
    {
        if (digitsCollected[orderIndex]) return; // Already collected (safety check).

        digitsCollected[orderIndex] = true;
        collectedCount++;

        Debug.Log($"[CodeNumberManager] Digit {orderIndex + 1}/4 collected: {digit}");

        if (hud != null) hud.UpdateSlot(orderIndex, digit, collectedCount);

        // Unlock the keypad once all 4 are found.
        if (collectedCount >= 4)
        {
            if (currentKeypad != null) currentKeypad.SetCodesCollected(true);
            if (hud != null) hud.ShowAllCollectedMessage();
            Debug.Log("[CodeNumberManager] All 4 digits collected. Keypad unlocked!");
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private void ClearPreviousNumbers()
    {
        // Destroy all existing CodeNumber objects from the previous level.
        CodeNumber[] existing = FindObjectsOfType<CodeNumber>();
        foreach (CodeNumber cn in existing)
            Destroy(cn.gameObject);
    }

    private void ResetState()
    {
        digitsCollected = new bool[4];
        collectedCount = 0;
        generatedDigits = new int[4];
        currentKeypad = null;
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
