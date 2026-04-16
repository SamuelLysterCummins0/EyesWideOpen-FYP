using UnityEngine;

/// <summary>
/// Placed at the top and bottom of each stairway connection.
/// Three instances exist per stairway, all parented to the persistent Stairways root
/// so they are always active regardless of which level is currently visible.
///
///   ShowLower  (inwardPos, upper level's Y):
///     Always ShowLevel(lower) — no-op if already visible.
///     Fires as the player enters the stairway door area going DOWN, or again as they
///     re-cross the door coming back from a partial descent.
///
///   HideLower  (twoStepPos = one tile deeper inside Level N, upper level's Y):
///     Always HideLevel(lower) — no-op if already hidden.
///     Fires only after the player has fully re-entered Level N's dungeon.
///     Separating this from ShowLower prevents the OnTriggerEnter re-entry problem:
///     the player must physically walk far enough past the door to fire HideLower,
///     so they will always exit and re-enter ShowLower on the next descent.
///
///   Bottom  (stairsPos, lower level's Y):
///     Uses IsLevelHidden to toggle the upper level:
///       Upper hidden → player ascending → ShowLevel(upper)
///       Upper visible → player descending → HideLevel(upper)
///
/// Created at runtime by DungeonLevelVisibility.SetupStairwayTriggers().
/// </summary>
public class StairwayVisibilityTrigger : MonoBehaviour
{
    public enum Role { ShowLower, HideLower, Bottom }

    private Role   role;
    private int    upperLevel;
    private int    lowerLevel;
    private DungeonLevelVisibility visibility;

    /// <summary>Called by DungeonLevelVisibility immediately after AddComponent.</summary>
    public void Initialise(Role r, int upper, int lower, DungeonLevelVisibility vis)
    {
        role       = r;
        upperLevel = upper;
        lowerLevel = lower;
        visibility = vis;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (visibility == null) return;

        switch (role)
        {
            case Role.ShowLower:
                // Unconditional — ShowLevel is a no-op if lower is already visible.
                // Fires when the player crosses the door tile toward the stairway (going down)
                // OR when re-crossing it from a partial ascent.
                visibility.ShowLevel(lowerLevel);
                break;

            case Role.HideLower:
                // Unconditional — HideLevel is a no-op if lower is already hidden.
                // Fires when the player has fully walked back into Level N's dungeon (going up),
                // or as they pass this tile approaching the stairway (going down, no-op).
                visibility.HideLevel(lowerLevel);
                break;

            case Role.Bottom:
                // State-driven: infer direction from whether the upper level is currently active.
                // Upper hidden  → player ascending  → reveal level above
                // Upper visible → player descending → conceal level above (arrived at lower level)
                if (visibility.IsLevelHidden(upperLevel))
                    visibility.ShowLevel(upperLevel);
                else
                    visibility.HideLevel(upperLevel);
                break;
        }
    }
}
