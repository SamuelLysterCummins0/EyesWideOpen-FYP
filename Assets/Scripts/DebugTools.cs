using UnityEngine;

/// <summary>
/// Debug shortcuts — remove or disable this component before shipping.
///
///   F1  — Acquire all 4 code numbers on the current level instantly.
///   F2  — Force power on (skips the powerbox interaction).
/// </summary>
public class DebugTools : MonoBehaviour
{
    [Header("Keys")]
    [SerializeField] private KeyCode acquireCodesKey = KeyCode.F1;
    [SerializeField] private KeyCode powerOnKey      = KeyCode.F2;

    private void Update()
    {
        if (Input.GetKeyDown(acquireCodesKey)) AcquireAllCodes();
        if (Input.GetKeyDown(powerOnKey))      ForcePowerOn();
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
}
