using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

public enum GazeZone
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    MidLeft = 3,
    Center = 4,
    MidRight = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8,
    None = -1
}

public class ZoneGazeDetector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GazeDetector gazeDetector;

    [Header("Zone Boundary Settings")]
    [Tooltip("X threshold for left zones (0.25 = easier to reach left edge)")]
    [SerializeField][Range(0.1f, 0.4f)] private float leftThreshold = 0.25f;

    [Tooltip("X threshold for right zones (0.75 = easier to reach right edge)")]
    [SerializeField][Range(0.6f, 0.9f)] private float rightThreshold = 0.75f;

    [Tooltip("Y threshold for top zones (0.25 = easier to reach top edge)")]
    [SerializeField][Range(0.1f, 0.4f)] private float topThreshold = 0.25f;

    [Tooltip("Y threshold for bottom zones (0.75 = easier to reach bottom edge)")]
    [SerializeField][Range(0.6f, 0.9f)] private float bottomThreshold = 0.75f;

    [Header("Zone Settings")]
    [SerializeField] private bool showZoneDebug = true;
    [SerializeField] private float zoneStabilityTime = 0.3f;

    [Header("Zone Events")]
    public UnityEvent<GazeZone> OnZoneEnter;
    public UnityEvent<GazeZone> OnZoneExit;
    public UnityEvent<GazeZone> OnZoneStable;

    // Current state
    private GazeZone currentZone = GazeZone.None;
    private GazeZone previousZone = GazeZone.None;
    private float timeInCurrentZone = 0f;
    private bool hasStabilized = false;

    // Debug visualization
    private GameObject debugCanvas;
    private Dictionary<GazeZone, GameObject> zoneHighlights = new Dictionary<GazeZone, GameObject>();

    public GazeZone CurrentZone => currentZone;
    public float TimeInZone => timeInCurrentZone;
    public bool IsStable => hasStabilized;

    private void Start()
    {
        if (gazeDetector == null)
        {
            gazeDetector = FindObjectOfType<GazeDetector>();
        }

        if (showZoneDebug)
        {
            CreateZoneDebugUI();
        }

        Debug.Log($"Zone boundaries: Left={leftThreshold}, Right={rightThreshold}, Top={topThreshold}, Bottom={bottomThreshold}");
    }

    private void Update()
    {
        if (gazeDetector == null || !gazeDetector.IsTracking)
        {
            UpdateZone(GazeZone.None);
            return;
        }

        Vector2 gaze = gazeDetector.GazeNormalized;
        GazeZone newZone = GetZoneFromNormalizedGaze(gaze);

        UpdateZone(newZone);

        if (showZoneDebug)
        {
            UpdateDebugVisualization();
        }
    }

    private GazeZone GetZoneFromNormalizedGaze(Vector2 normalizedGaze)
    {
        // Clamp to 0-1
        normalizedGaze.x = Mathf.Clamp01(normalizedGaze.x);
        normalizedGaze.y = Mathf.Clamp01(normalizedGaze.y);

        // Determine column (X axis)
        int col = 1; // Default to center
        if (normalizedGaze.x < leftThreshold)
            col = 0; // Left
        else if (normalizedGaze.x > rightThreshold)
            col = 2; // Right

        // Determine row (Y axis)
        int row = 1; // Default to middle
        if (normalizedGaze.y < topThreshold)
            row = 0; // Top
        else if (normalizedGaze.y > bottomThreshold)
            row = 2; // Bottom

        int zoneIndex = row * 3 + col;
        return (GazeZone)zoneIndex;
    }

    private void UpdateZone(GazeZone newZone)
    {
        if (newZone == currentZone)
        {
            if (currentZone != GazeZone.None)
            {
                timeInCurrentZone += Time.deltaTime;

                if (!hasStabilized && timeInCurrentZone >= zoneStabilityTime)
                {
                    hasStabilized = true;
                    OnZoneStable?.Invoke(currentZone);
                    Debug.Log($"Zone stabilized: {currentZone}");
                }
            }
        }
        else
        {
            if (currentZone != GazeZone.None)
            {
                OnZoneExit?.Invoke(currentZone);
                Debug.Log($"Zone exit: {currentZone}");
            }

            previousZone = currentZone;
            currentZone = newZone;
            timeInCurrentZone = 0f;
            hasStabilized = false;

            if (currentZone != GazeZone.None)
            {
                OnZoneEnter?.Invoke(currentZone);
                Debug.Log($"Zone enter: {currentZone}");
            }
        }
    }

    public Vector2 GetZoneCenter(GazeZone zone)
    {
        if (zone == GazeZone.None) return Vector2.one * 0.5f;

        int zoneIndex = (int)zone;
        int row = zoneIndex / 3;
        int col = zoneIndex % 3;

        // Calculate center based on actual thresholds
        float centerX = 0.5f;
        if (col == 0)
            centerX = leftThreshold / 2f; // Middle of left zone
        else if (col == 2)
            centerX = (rightThreshold + 1f) / 2f; // Middle of right zone

        float centerY = 0.5f;
        if (row == 0)
            centerY = topThreshold / 2f; // Middle of top zone
        else if (row == 2)
            centerY = (bottomThreshold + 1f) / 2f; // Middle of bottom zone

        return new Vector2(centerX, centerY);
    }

    public Vector2 GetZoneCenterScreen(GazeZone zone)
    {
        Vector2 normalized = GetZoneCenter(zone);
        return new Vector2(
            normalized.x * Screen.width,
            (1f - normalized.y) * Screen.height
        );
    }

    public Rect GetZoneBounds(GazeZone zone)
    {
        if (zone == GazeZone.None) return new Rect(0, 0, 1, 1);

        int zoneIndex = (int)zone;
        int row = zoneIndex / 3;
        int col = zoneIndex % 3;

        float xMin = 0f, xMax = 1f, yMin = 0f, yMax = 1f;

        // Calculate X bounds
        if (col == 0)
        {
            xMin = 0f;
            xMax = leftThreshold;
        }
        else if (col == 1)
        {
            xMin = leftThreshold;
            xMax = rightThreshold;
        }
        else // col == 2
        {
            xMin = rightThreshold;
            xMax = 1f;
        }

        // Calculate Y bounds
        if (row == 0)
        {
            yMin = 0f;
            yMax = topThreshold;
        }
        else if (row == 1)
        {
            yMin = topThreshold;
            yMax = bottomThreshold;
        }
        else // row == 2
        {
            yMin = bottomThreshold;
            yMax = 1f;
        }

        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    public string GetZoneName(GazeZone zone)
    {
        return zone.ToString().Replace("Top", "Top ").Replace("Mid", "Mid ").Replace("Bottom", "Bottom ");
    }

    private void CreateZoneDebugUI()
    {
        debugCanvas = new GameObject("ZoneDebugCanvas");
        Canvas canvas = debugCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        UnityEngine.UI.CanvasScaler scaler = debugCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        for (int i = 0; i < 9; i++)
        {
            GazeZone zone = (GazeZone)i;
            GameObject zoneObj = CreateZoneHighlight(zone, canvas.transform);
            zoneHighlights[zone] = zoneObj;
        }
    }

    private GameObject CreateZoneHighlight(GazeZone zone, Transform parent)
    {
        GameObject obj = new GameObject($"Zone_{zone}");
        obj.transform.SetParent(parent, false);

        UnityEngine.UI.Image img = obj.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(1f, 1f, 0f, 0.1f);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);

        // Calculate zone bounds based on thresholds
        Rect bounds = GetZoneBounds(zone);

        float width = bounds.width * Screen.width;
        float height = bounds.height * Screen.height;
        rect.sizeDelta = new Vector2(width - 10, height - 10);

        Vector2 center = GetZoneCenterScreen(zone);
        rect.position = center;

        // Add text label
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(obj.transform, false);

        UnityEngine.UI.Text text = textObj.AddComponent<UnityEngine.UI.Text>();
        text.text = GetZoneName(zone);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 1f, 0f, 0.5f);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        obj.SetActive(true);
        return obj;
    }

    private void UpdateDebugVisualization()
    {
        foreach (var kvp in zoneHighlights)
        {
            GazeZone zone = kvp.Key;
            GameObject highlight = kvp.Value;
            UnityEngine.UI.Image img = highlight.GetComponent<UnityEngine.UI.Image>();

            if (zone == currentZone)
            {
                float intensity = hasStabilized ? 0.5f : Mathf.Lerp(0.1f, 0.3f, timeInCurrentZone / zoneStabilityTime);
                img.color = new Color(0f, 1f, 0f, intensity);
            }
            else if (zone == previousZone)
            {
                img.color = new Color(1f, 0.5f, 0f, 0.15f);
            }
            else
            {
                img.color = new Color(1f, 1f, 0f, 0.05f);
            }
        }
    }

    public void SetDebugVisible(bool visible)
    {
        showZoneDebug = visible;
        if (debugCanvas != null)
        {
            debugCanvas.SetActive(visible);
        }
    }

    // Helper method to adjust thresholds at runtime
    public void SetThresholds(float left, float right, float top, float bottom)
    {
        leftThreshold = Mathf.Clamp(left, 0.1f, 0.4f);
        rightThreshold = Mathf.Clamp(right, 0.6f, 0.9f);
        topThreshold = Mathf.Clamp(top, 0.1f, 0.4f);
        bottomThreshold = Mathf.Clamp(bottom, 0.6f, 0.9f);

        Debug.Log($"Thresholds updated: L={leftThreshold}, R={rightThreshold}, T={topThreshold}, B={bottomThreshold}");

        // Recreate zone highlights with new boundaries
        if (showZoneDebug && debugCanvas != null)
        {
            Destroy(debugCanvas);
            zoneHighlights.Clear();
            CreateZoneDebugUI();
        }
    }
}