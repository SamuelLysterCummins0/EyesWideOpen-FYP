using UnityEngine;

/// <summary>
/// Debug shortcuts — remove or disable this component before shipping.
///
///   F1  — Acquire all 4 code numbers on the current level instantly.
///   F2  — Force power on (skips the powerbox interaction).
///   F3  — Instantly trigger the Siren Phase (skips idle wait).
///   F5  — Skip intro room: activates gaze tracking and teleports the player
///          directly to the Level 0 dungeon spawn room.
/// </summary>
public class DebugTools : MonoBehaviour
{
    [Header("Keys")]
    [SerializeField] private KeyCode acquireCodesKey  = KeyCode.F1;
    [SerializeField] private KeyCode powerOnKey       = KeyCode.F2;
    [SerializeField] private KeyCode sirenKey         = KeyCode.F3;
    [SerializeField] private KeyCode skipIntroKey     = KeyCode.F5;

    private void Update()
    {
        if (Input.GetKeyDown(acquireCodesKey))  AcquireAllCodes();
        if (Input.GetKeyDown(powerOnKey))       ForcePowerOn();
        if (Input.GetKeyDown(sirenKey))         ForceSiren();
        if (Input.GetKeyDown(skipIntroKey))     SkipIntroRoom();
    }

    private void AcquireAllCodes()
    {
        if (CodeNumberManager.Instance == null || GameManager.Instance == null)
        {
            Debug.LogWarning("[DebugTools] CodeNumberManager or GameManager not found.");
            return;
        }

        int level = GameManager.Instance.GetCurrentLevel();
        for (int i = 0; i < 4; i++)
        {
            int digit = CodeNumberManager.Instance.GetDigit(level, i);
            if (digit >= 0)
                CodeNumberManager.Instance.OnDigitCollected(level, i, digit);
        }

        Debug.Log($"[DebugTools] Acquired all 4 codes for level {level}.");
    }

    private void ForcePowerOn()
    {
        if (PowerManager.Instance == null)
        {
            Debug.LogWarning("[DebugTools] PowerManager not found.");
            return;
        }

        PowerManager.Instance.TurnOnPower();
        Debug.Log("[DebugTools] Power forced on.");
    }

    private void ForceSiren()
    {
        if (SirenPhaseManager.Instance == null)
        {
            Debug.LogWarning("[DebugTools] SirenPhaseManager not found.");
            return;
        }

        SirenPhaseManager.Instance.ForceTriggerSiren();
        Debug.Log("[DebugTools] Siren phase force-triggered.");
    }

    private void SkipIntroRoom()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[DebugTools] GameManager not found.");
            return;
        }

        GameManager.Instance.SkipToSpawnRoom();
    }
}
