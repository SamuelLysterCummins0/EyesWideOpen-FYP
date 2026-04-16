/// <summary>
/// Tracks two separate protection states for the player:
///
///   IsPlayerInRoom      — player is physically inside a safe/spawn/computer room,
///                         regardless of door state. PacerNPC uses this to break
///                         chase the moment the player enters any protected room.
///
///   IsPlayerProtected   — player is inside a room AND all doors are closed.
///                         Used by RoomNPCShuffle to trigger the NPC-shuffle and
///                         by the legacy "fully enclosed" protection logic.
///
/// Lockers are handled separately via LockerInteraction.IsHidingInLocker.
/// </summary>
public static class PlayerSafeZone
{
    // ── Door-closed protection (existing) ─────────────────────────────────────
    private static int _closedCount = 0;

    /// <summary>True when the player is inside at least one protected room with all doors closed.</summary>
    public static bool IsPlayerProtected => _closedCount > 0;

    /// <summary>Called by RoomNPCShuffle when the player is inside AND all doors are closed.</summary>
    public static void RegisterProtection() => _closedCount++;

    /// <summary>Called by RoomNPCShuffle when the player leaves or opens a door.</summary>
    public static void UnregisterProtection() => _closedCount = _closedCount > 0 ? _closedCount - 1 : 0;

    // ── Room-entry tracking (new) ─────────────────────────────────────────────
    private static int _roomCount = 0;

    /// <summary>
    /// True when the player is physically inside a safe/spawn/computer room,
    /// regardless of whether the doors are open or closed.
    /// PacerNPC checks this so it breaks chase the moment the player steps inside,
    /// preventing the "running in place at the door frame" bug.
    /// </summary>
    public static bool IsPlayerInRoom => _roomCount > 0;

    /// <summary>Called by RoomNPCShuffle when the player enters the room radius.</summary>
    public static void RegisterRoomEntry() => _roomCount++;

    /// <summary>Called by RoomNPCShuffle when the player exits the room radius.</summary>
    public static void UnregisterRoomEntry() => _roomCount = _roomCount > 0 ? _roomCount - 1 : 0;

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>Hard-reset — called on dungeon regeneration or scene reload.</summary>
    public static void Reset()
    {
        _closedCount = 0;
        _roomCount   = 0;
    }
}
