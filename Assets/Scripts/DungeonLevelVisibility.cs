using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls which dungeon level GameObjects are active (visible and simulated) at any time.
///
/// All show/hide is now fully trigger-driven via StairwayVisibilityTrigger — no polling.
///
/// Flow:
///   1. ProceduralDungeonGenerator calls RegisterLevel() after each level is built.
///   2. InitialHide() is called once after ALL levels are generated and NavMesh is baked.
///      Hides every level except level 0, then calls SetupStairwayTriggers().
///   3. StairwayVisibilityTrigger instances call ShowLevel / HideLevel as the player
///      enters the top or bottom of each stairway.
///
/// Attach to the same GameObject as ProceduralDungeonGenerator.
/// </summary>
public class DungeonLevelVisibility : MonoBehaviour
{
    // Level parent GameObjects indexed by level number
    private readonly Dictionary<int, GameObject> registeredLevels = new Dictionary<int, GameObject>();

    // Stairway GOs collected via RegisterStairway — reparented to the persistent root in InitialHide
    private readonly List<GameObject> registeredStairways = new List<GameObject>();

    // Persistent root that lives outside all level parents — its children are never hidden
    private GameObject stairwaysRoot;

    private float levelHeight  = 4f;
    private int   dungeonWidth = 12;   // needed by GetStepInward to detect the right-hand wall
    private bool  initialised  = false;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Vertical distance between levels — used by NPCSpawnManager to determine
    /// which level a spawned object belongs to so it can be parented correctly.</summary>
    public float LevelHeight => levelHeight;

    /// <summary>Returns the level parent GameObject for the given level index, or null.</summary>
    public GameObject GetLevelParent(int levelIndex)
    {
        registeredLevels.TryGetValue(levelIndex, out GameObject parent);
        return parent;
    }

    /// <summary>
    /// Called by ProceduralDungeonGenerator after each level is fully generated.
    /// Does NOT hide — call InitialHide() once all levels are done.
    /// </summary>
    public void RegisterLevel(int levelIndex, GameObject levelParent)
    {
        registeredLevels[levelIndex] = levelParent;
    }

    /// <summary>
    /// Called by ProceduralDungeonGenerator immediately after placing each stairway tile.
    /// The GO is later reparented to the persistent Stairways root so it is never hidden
    /// when its original level parent is deactivated.
    /// </summary>
    public void RegisterStairway(GameObject stairwayGO)
    {
        if (stairwayGO != null)
            registeredStairways.Add(stairwayGO);
    }

    /// <summary>
    /// Called once after all levels are generated and NavMesh is baked.
    /// Hides everything except level 0, then plants stairway triggers.
    /// </summary>
    public void InitialHide(float dungeonLevelHeight, int width,
                            List<Vector2Int> stairsPositions, List<GameObject> levelParents,
                            float tileSize)
    {
        levelHeight  = dungeonLevelHeight;
        dungeonWidth = width;
        initialised  = true;

        // Create the persistent root that sits outside all level parents.
        // Stairway tiles and triggers are reparented here so they survive level hide/show.
        if (stairwaysRoot != null) SafeDestroy(stairwaysRoot);
        stairwaysRoot = new GameObject("Stairways");

        // Reparent every stairway tile out of its level parent before levels are hidden.
        // Must happen BEFORE SetActive(false) calls below — a child can't be reparented
        // out of an inactive parent without temporarily reactivating it, so we do it first.
        foreach (GameObject stairway in registeredStairways)
        {
            if (stairway != null)
                stairway.transform.SetParent(stairwaysRoot.transform, worldPositionStays: true);
        }

        // Setup triggers BEFORE hiding levels — triggers are parented to stairwaysRoot
        // inside SetupStairwayTriggers so they're also permanently active.
        SetupStairwayTriggers(stairsPositions, levelParents, tileSize);

        // Now safe to hide all levels except 0 — stairways and triggers are already out.
        foreach (var kvp in registeredLevels)
            SetLevelActive(kvp.Key, kvp.Key == 0);

        Debug.Log($"[DungeonLevelVisibility] InitialHide: {registeredLevels.Count} levels, " +
                  $"{registeredStairways.Count} stairways extracted to persistent root.");
    }

    /// <summary>Show a level — called by StairwayVisibilityTrigger.</summary>
    public void ShowLevel(int levelIndex)
    {
        SetLevelActive(levelIndex, true);
    }

    /// <summary>Hide a level — called by StairwayVisibilityTrigger.</summary>
    public void HideLevel(int levelIndex)
    {
        SetLevelActive(levelIndex, false);
    }

    /// <summary>
    /// Show exactly one level and hide all others — used by GameManager.RegenerateDungeon
    /// to force the visibility state to match "player is on level N" after a full rebuild.
    /// Without this call, InitialHide() leaves every non-zero level inactive and any
    /// teleport to level 1+ would drop the player through non-existent geometry.
    /// </summary>
    public void ShowOnlyLevel(int levelIndex)
    {
        foreach (var kvp in registeredLevels)
            SetLevelActive(kvp.Key, kvp.Key == levelIndex);
    }

    /// <summary>
    /// Returns true if the level is not currently active (hidden or not yet registered).
    /// Used by StairwayVisibilityTrigger to infer travel direction without velocity checks:
    /// if the lower level is hidden when the player hits the Top trigger, they must be
    /// descending; if it is visible, they must be ascending back up.
    /// </summary>
    public bool IsLevelHidden(int levelIndex)
    {
        if (!registeredLevels.TryGetValue(levelIndex, out GameObject parent) || parent == null)
            return true;   // not registered or already destroyed → treat as hidden
        return !parent.activeSelf;
    }

    /// <summary>
    /// Clears all registered data. Called by ProceduralDungeonGenerator.ClearDungeon()
    /// before level parents are destroyed so stale references don't linger.
    /// </summary>
    public void ClearAll()
    {
        // Destroy the persistent Stairways root — its children (stairway tiles + triggers)
        // will be destroyed with it. The level parents are destroyed separately by
        // ProceduralDungeonGenerator.ClearDungeon() so stairway tiles are not double-destroyed.
        SafeDestroy(stairwaysRoot);
        stairwaysRoot = null;

        registeredStairways.Clear();
        registeredLevels.Clear();
        initialised = false;
    }

    private void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }

    // ── Trigger placement ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates three BoxCollider triggers per stairway connection, all parented to
    /// the persistent stairwaysRoot so they are always active:
    ///
    ///   ShowLower  (inwardPos, upper level's Y)   — always ShowLevel(lower)
    ///   HideLower  (twoStepPos, upper level's Y)  — always HideLevel(lower)
    ///   Bottom     (stairsPos,  lower level's Y)  — state-driven Show/Hide for upper level
    ///
    /// Splitting Show and Hide into separate triggers at different positions fixes the
    /// OnTriggerEnter re-entry problem: the player must physically walk past HideLower
    /// before they can re-enter ShowLower on the next descent.
    /// </summary>
    private void SetupStairwayTriggers(List<Vector2Int> stairsPositions,
                                       List<GameObject>  levelParents,
                                       float             tileSize)
    {
        if (stairsPositions == null) return;

        for (int i = 0; i < stairsPositions.Count; i++)
        {
            int upperLevel = i;
            int lowerLevel = i + 1;

            if (lowerLevel >= levelParents.Count) break;

            GameObject upperParent = levelParents[upperLevel];
            GameObject lowerParent = levelParents[lowerLevel];
            if (upperParent == null || lowerParent == null) continue;

            Vector2Int stairsPos = stairsPositions[i];
            Vector2Int inwardPos = GetStepInward(stairsPos);   // 1 step inside Level N (door tile)

            float upperY = upperLevel * -levelHeight;
            float lowerY = lowerLevel * -levelHeight;

            // ShowLower — at the stairway tile (stairsPos), upper level's Y.
            // The player MUST step onto the ramp tile to descend/ascend so this always fires.
            // Fires ShowLevel(lower) when the player steps onto the stairway.
            // Going DOWN: this is the SECOND trigger the player hits (fires Show) ✓
            // Going UP:   this is the FIRST  trigger the player hits (fires no-op, L+1 still visible) ✓
            Vector3 showWorld = new Vector3(stairsPos.x * tileSize, upperY, stairsPos.y * tileSize);
            CreateTrigger($"StairTrigger_ShowLower_{i}", showWorld, stairwaysRoot,
                          StairwayVisibilityTrigger.Role.ShowLower, upperLevel, lowerLevel, tileSize);

            // HideLower — at the door tile (inwardPos), upper level's Y.
            // Placed here (not two tiles deep) so the safe-room door never blocks the player
            // from reaching it. The trigger fires on proximity even if the door is closed.
            // Fires HideLevel(lower) when the player passes back through the door into Level N.
            // Going DOWN: this is the FIRST  trigger the player hits (fires no-op, L+1 hidden) ✓
            // Going UP:   this is the SECOND trigger the player hits (fires Hide) ✓
            Vector3 hideWorld = new Vector3(inwardPos.x * tileSize, upperY, inwardPos.y * tileSize);
            CreateTrigger($"StairTrigger_HideLower_{i}", hideWorld, stairwaysRoot,
                          StairwayVisibilityTrigger.Role.HideLower, upperLevel, lowerLevel, tileSize);

            // Bottom — at the stairway tile (stairsPos), lower level's Y.
            // The player MUST step here to use the ramp — cannot be bypassed.
            // State-driven: upper hidden → ascending → ShowLevel(upper)
            //               upper visible → descending → HideLevel(upper)
            Vector3 bottomWorld = new Vector3(stairsPos.x * tileSize, lowerY, stairsPos.y * tileSize);
            CreateTrigger($"StairTrigger_Bottom_{i}", bottomWorld, stairwaysRoot,
                          StairwayVisibilityTrigger.Role.Bottom, upperLevel, lowerLevel, tileSize);

            Debug.Log($"[DungeonLevelVisibility] Stairway triggers placed for L{upperLevel}↔L{lowerLevel} " +
                      $"(showLower/rampTop:{stairsPos}@Y{upperY}  hideLower/door:{inwardPos}@Y{upperY}  bottom:{stairsPos}@Y{lowerY})");
        }
    }

    private void CreateTrigger(string objName, Vector3 worldPos, GameObject parent,
                               StairwayVisibilityTrigger.Role role,
                               int upperLevel, int lowerLevel, float tileSize)
    {
        GameObject obj = new GameObject(objName);
        obj.layer = 2; // "Ignore Raycast" — prevents these triggers from blocking gaze raycasts
        obj.transform.SetParent(parent.transform);
        obj.transform.position = worldPos;

        BoxCollider col = obj.AddComponent<BoxCollider>();
        // Height 3 (< levelHeight 4) ensures the Top and Bottom triggers for the same
        // stairway never overlap in Y, preventing a double-fire when the player is midway
        // on the ramp.  Y-ranges with levelHeight=4:
        //   Top    centre=upperY → [upperY-1.5 … upperY+1.5]
        //   Bottom centre=lowerY → [lowerY-1.5 … lowerY+1.5]  (1-unit gap between them)
        col.size      = new Vector3(tileSize * 0.9f, 3f, tileSize * 0.9f);
        col.isTrigger = true;

        StairwayVisibilityTrigger trigger = obj.AddComponent<StairwayVisibilityTrigger>();
        trigger.Initialise(role, upperLevel, lowerLevel, this);
    }

    // One step inward from a perimeter position — mirrors SafeRoomSetup.GetEntranceTile.
    // Used to place the Top trigger at the safe-room door rather than at the stairway tile.
    private Vector2Int GetStepInward(Vector2Int pos)
    {
        if (pos.x == 0)                return pos + new Vector2Int( 1,  0);
        if (pos.x == dungeonWidth - 1) return pos + new Vector2Int(-1,  0);
        if (pos.y == 0)                return pos + new Vector2Int( 0,  1);
        return                                pos + new Vector2Int( 0, -1);
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private void SetLevelActive(int levelIndex, bool active)
    {
        if (!registeredLevels.TryGetValue(levelIndex, out GameObject parent) || parent == null)
            return;
        if (parent.activeSelf == active) return;

        parent.SetActive(active);
        Debug.Log($"[DungeonLevelVisibility] Level {levelIndex} → {(active ? "VISIBLE" : "HIDDEN")}");
    }
}
