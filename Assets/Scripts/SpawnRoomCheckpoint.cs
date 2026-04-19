using System.Collections;
using UnityEngine;

// Placed at the stairway entrance tile of each sub-level by SpawnRoomSetup.
// When the player walks into this trigger, that level becomes their active respawn point —
// so dying from now on sends them back here instead of level 0.
//
// Uses OnTriggerEnter (event-driven) rather than Update polling for zero per-frame cost.
public class SpawnRoomCheckpoint : MonoBehaviour
{
    // Set by SpawnRoomSetup.SpawnCheckpointTrigger() at generation time.
    private int levelIndex = -1;

    [SerializeField] private float notificationDuration = 2.5f;

    // Prevents re-triggering every time the player walks in and out.
    private bool isActivated = false;

    /// <summary>Exposes the level this checkpoint belongs to (read by SaveGameManager).</summary>
    public int LevelIndex => levelIndex;

    /// <summary>
    /// Called by SaveGameManager on load to silently mark this checkpoint as already activated,
    /// preventing the "Checkpoint saved" notification from firing again when the player
    /// re-enters a room they already visited before saving.
    /// Does NOT call SetCurrentLevel — that is handled separately by SaveGameManager.
    /// </summary>
    public void MarkActivated()
    {
        isActivated = true;
    }

    // Optional on-screen message — created at runtime, no scene setup needed.
    private static GameObject notificationCanvas;

    public void Initialise(int level)
    {
        levelIndex = level;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isActivated) return;
        if (!other.CompareTag("Player")) return;
        if (levelIndex < 0) return;

        isActivated = true;

        if (GameManager.Instance != null)
            GameManager.Instance.SetCurrentLevel(levelIndex);

        // When the player first drops into the dungeon from the intro room,
        // restore the HUD elements that were hidden during the intro sequence
        // and disable the intro room geometry for performance.
        if (levelIndex == 0)
        {
            HUDManager.Instance?.SetIntroMode(false);
            IntroRoomSetup.Instance?.DisableIntroRoom();
        }

        Debug.Log($"SpawnRoomCheckpoint: Level {levelIndex} checkpoint activated. Respawn point updated.");

        StartCoroutine(ShowNotification("Checkpoint saved"));
    }

    // Displays a brief screen notification without requiring a pre-wired UI canvas.
    private IEnumerator ShowNotification(string message)
    {
        // Reuse an existing notification canvas if one is already alive.
        if (notificationCanvas == null)
        {
            notificationCanvas = new GameObject("CheckpointNotificationCanvas");
            Canvas canvas = notificationCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            notificationCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
            notificationCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            DontDestroyOnLoad(notificationCanvas);
        }

        // Build the text label.
        GameObject textObj = new GameObject("CheckpointText");
        textObj.transform.SetParent(notificationCanvas.transform, false);

        UnityEngine.UI.Text text = textObj.AddComponent<UnityEngine.UI.Text>();
        text.text = message;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 28;
        text.color = new Color(0.6f, 1f, 0.6f, 1f); // soft green
        text.alignment = TextAnchor.MiddleCenter;

        RectTransform rt = textObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.15f);
        rt.anchorMax = new Vector2(0.5f, 0.15f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(400f, 50f);

        // Fade in.
        float fadeIn = 0.3f;
        for (float t = 0; t < fadeIn; t += Time.deltaTime)
        {
            text.color = new Color(0.6f, 1f, 0.6f, t / fadeIn);
            yield return null;
        }
        text.color = new Color(0.6f, 1f, 0.6f, 1f);

        yield return new WaitForSeconds(notificationDuration);

        // Fade out.
        float fadeOut = 0.5f;
        for (float t = 0; t < fadeOut; t += Time.deltaTime)
        {
            text.color = new Color(0.6f, 1f, 0.6f, 1f - (t / fadeOut));
            yield return null;
        }

        Destroy(textObj);
    }
}
