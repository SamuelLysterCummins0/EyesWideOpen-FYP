using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;

public class GazeDetector : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float smoothingFactor = 0.3f;
    [SerializeField] private bool showDebugCursor = true;

    // Nose tip - most stable face landmark
    private const int FACE_CENTER = 1;

    private Vector2 rawPosition = new Vector2(0.5f, 0.5f);
    private Vector2 smoothedPosition = new Vector2(0.5f, 0.5f);
    private Vector2 screenPosition = Vector2.zero;

    private GameObject debugCursor;
    private RectTransform debugCursorRect;

    public Vector2 GazeNormalized => smoothedPosition;
    public Vector2 ScreenPosition => screenPosition;
    public bool IsTracking { get; private set; }

    private void Start()
    {
        if (showDebugCursor)
        {
            CreateDebugCursor();
        }
    }

    private void Update()
    {
        screenPosition = new Vector2(
            smoothedPosition.x * Screen.width,
            (1f - smoothedPosition.y) * Screen.height
        );

        if (debugCursor != null && debugCursorRect != null)
        {
            debugCursorRect.position = screenPosition;
        }
    }

    public void ProcessLandmarks(List<NormalizedLandmark> landmarks)
    {
        if (landmarks == null || landmarks.Count < 478)
        {
            IsTracking = false;
            return;
        }

        IsTracking = true;

        // Just track nose tip position
        rawPosition = new Vector2(
            landmarks[FACE_CENTER].x,
            landmarks[FACE_CENTER].y
        );

        rawPosition.x = Mathf.Clamp01(rawPosition.x);
        rawPosition.y = Mathf.Clamp01(rawPosition.y);

        smoothedPosition = Vector2.Lerp(smoothedPosition, rawPosition, smoothingFactor);
    }



    private void CreateDebugCursor()
    {
        GameObject canvasObj = new GameObject("GazeCursorCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Main cursor (red circle) - NO BACKGROUND
        debugCursor = new GameObject("GazeCursor");
        debugCursor.transform.SetParent(canvas.transform);
        var image = debugCursor.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(1f, 0f, 0f, 0.8f); // Red
        image.raycastTarget = false;
        image.sprite = CreateCircleSprite();

        debugCursorRect = debugCursor.GetComponent<RectTransform>();
        debugCursorRect.sizeDelta = new Vector2(20, 20); // Small size
        debugCursorRect.anchorMin = Vector2.zero;
        debugCursorRect.anchorMax = Vector2.zero;
        debugCursorRect.pivot = new Vector2(0.5f, 0.5f);

        Debug.Log("Gaze cursor created");
    }

    private Sprite CreateCircleSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);

                if (distance <= radius)
                {
                    // Smooth edge - RED color, not white
                    float alpha = 1f - Mathf.Clamp01((distance - radius + 2f) / 2f);
                    pixels[y * size + x] = new Color(1f, 0f, 0f, alpha); // Changed from white to red
                }
                else
                {
                    pixels[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    public void SetDebugCursorVisible(bool visible)
    {
        if (debugCursor != null)
        {
            debugCursor.SetActive(visible);
        }
    }

}