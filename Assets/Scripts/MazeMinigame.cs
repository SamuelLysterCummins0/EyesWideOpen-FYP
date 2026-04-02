using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class MazeMinigame : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GazeDetector gazeDetector;
    [SerializeField] private GameObject mazeCanvasRoot;
    [SerializeField] private RectTransform mazeCursorRect;
    [SerializeField] private RectTransform mazeContainerRect;
    [SerializeField] private TMP_Text statusText;

    [Header("Maze Size")]
    [SerializeField] private int   cols          = 8;
    [SerializeField] private int   rows          = 6;
    [SerializeField] private float cellSize      = 110f;   // larger cells = wider corridors
    [SerializeField] private float wallThickness = 12f;

    [Header("Colors")]
    [SerializeField] private Color wallColor    = new Color(0.15f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color startColor   = new Color(0f,    1f,    0.4f,  1f);
    [SerializeField] private Color endColor     = new Color(0f,    1f,    0.5f,  0.6f);
    [SerializeField] private Color normalCursor = new Color(0.2f,  1f,    0.3f,  0.9f);
    [SerializeField] private Color hitCursor    = Color.yellow;
    [SerializeField] private Color winCursor    = Color.green;

    [Header("Settings")]
    [SerializeField] private float restartDelay = 0.8f;
    [Tooltip("Scales cursor movement range from canvas centre. 1 = full range, 0.5 = cursor only reaches halfway across the maze. Lower = less sensitive.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float cursorSensitivity = 0.6f;

    // Set by ComputerInteraction.StartInteraction() BEFORE calling StartMaze()
    // so that ScreenPointToLocalPointInRectangle maps correctly onto the World Space canvas
    public Camera mazeCamera;

    // Read by ComputerInteraction after the first StartMaze() call to save the layout
    public bool[,] HWalls => hWalls;
    public bool[,] VWalls => vWalls;

    // Set by ComputerInteraction so we know which level's digit to reveal on win
    public int levelIndex = 0;

    public System.Action OnMazeSolvedCallback;

    // --- Maze data ---
    // hWalls[c, r] = wall on BOTTOM edge of cell (c, r).  Size: [cols, rows+1]
    // vWalls[c, r] = wall on LEFT  edge of cell (c, r).  Size: [cols+1, rows]
    private bool[,] hWalls;
    private bool[,] vWalls;
    private bool[,] visited;

    // --- Generated scene objects ---
    private readonly List<GameObject>    generatedObjects = new List<GameObject>();
    private readonly List<RectTransform> wallRects        = new List<RectTransform>();
    private RectTransform startMarkerRect;
    private RectTransform endZoneRect;
    private Image         startMarkerImage;

    // --- Runtime state ---
    private Image   cursorImage;
    private bool    isActive          = false;
    private bool    isSolving         = false;
    private bool    isWaitingForStart = false;
    private Vector2 startLocalPos;
    private Vector2 smoothedLocalPos; // current cursor position in container-local space

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (mazeCursorRect != null)
            cursorImage = mazeCursorRect.GetComponent<Image>();

        if (gazeDetector == null)
            gazeDetector = FindObjectOfType<GazeDetector>();

        // Use CanvasGroup to hide/show — canvas stays active at all times so
        // no Start() timing issues. Alpha=0 makes it invisible and non-interactive.
        if (mazeCanvasRoot != null)
        {
            CanvasGroup cg = mazeCanvasRoot.GetComponent<CanvasGroup>();
            if (cg == null) cg = mazeCanvasRoot.AddComponent<CanvasGroup>();
            cg.alpha          = 0f;
            cg.interactable   = false;
            cg.blocksRaycasts = false;
        }
    }

    // Called by ComputerInteraction.StartInteraction()
    // screenFace = the ScreenFace child Transform on the computer prefab —
    // its position is the screen surface centre, its forward is the screen's outward normal.
    // existingHWalls / existingVWalls: pass the saved arrays from ComputerInteraction to
    // restore the same layout on re-entry instead of generating a fresh random maze.
    public void StartMaze(Transform screenFace, bool[,] existingHWalls = null, bool[,] existingVWalls = null)
    {
        ClearMaze();

        if (existingHWalls != null && existingVWalls != null)
        {
            // Restore saved layout — player gets the same maze every time they re-enter
            hWalls = existingHWalls;
            vWalls = existingVWalls;
        }
        else
        {
            GenerateMaze();
        }

        BuildMazeVisuals();

        if (mazeCanvasRoot != null)
        {
            // Snap canvas to this computer's screen face
            if (screenFace != null)
            {
                mazeCanvasRoot.transform.position = screenFace.position;
                mazeCanvasRoot.transform.rotation = screenFace.rotation;
            }

            // Assign camera so ScreenPointToLocalPointInRectangle projects correctly
            Canvas canvas = mazeCanvasRoot.GetComponent<Canvas>();
            if (canvas != null && mazeCamera != null)
                canvas.worldCamera = mazeCamera;

            // Make visible — canvas was never disabled so no timing issues
            CanvasGroup cg = mazeCanvasRoot.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; }
        }

        
        startLocalPos = startMarkerRect != null ? startMarkerRect.anchoredPosition : Vector2.zero;

        isActive         = true;
        isSolving        = false;
        smoothedLocalPos = Vector2.zero;

        if (gazeDetector != null)
            gazeDetector.SetDebugCursorVisible(false);

        EnterWaitForStart();

        Debug.Log($"MazeMinigame: {cols}x{rows} maze started. {wallRects.Count} wall segments.");
    }

    // Called by ComputerInteraction.EndInteraction()
    public void StopMaze()
    {
        isActive = false;
        StopAllCoroutines();

        // Restore cursor visibility in case WinSequence hid it
        if (mazeCursorRect != null) mazeCursorRect.gameObject.SetActive(true);

        if (mazeCanvasRoot != null)
        {
            CanvasGroup cg = mazeCanvasRoot.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 0f; cg.blocksRaycasts = false; }
        }

        if (gazeDetector != null)
            gazeDetector.SetDebugCursorVisible(true);
    }

    private void ClearMaze()
    {
        foreach (GameObject go in generatedObjects)
            if (go != null) Destroy(go);

        generatedObjects.Clear();
        wallRects.Clear();
        startMarkerRect = null;
        endZoneRect     = null;
    }

    // ─── Maze Generation — Recursive Backtracking ────────────────────────

    private void GenerateMaze()
    {
        hWalls  = new bool[cols,     rows + 1];
        vWalls  = new bool[cols + 1, rows    ];
        visited = new bool[cols,     rows    ];

        for (int c = 0; c < cols;     c++)
            for (int r = 0; r <= rows; r++)
                hWalls[c, r] = true;

        for (int c = 0; c <= cols; c++)
            for (int r = 0; r < rows;  r++)
                vWalls[c, r] = true;

        Carve(0, 0);
    }

    private void Carve(int col, int row)
    {
        visited[col, row] = true;

        int[] dirs = { 0, 1, 2, 3 };
        for (int i = 3; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = dirs[i]; dirs[i] = dirs[j]; dirs[j] = tmp;
        }

        foreach (int dir in dirs)
        {
            int nc = col, nr = row;
            if      (dir == 0) nr++;
            else if (dir == 1) nr--;
            else if (dir == 2) nc++;
            else               nc--;

            if (nc < 0 || nc >= cols || nr < 0 || nr >= rows || visited[nc, nr]) continue;

            if      (dir == 0) hWalls[col,     row + 1] = false;
            else if (dir == 1) hWalls[col,     row    ] = false;
            else if (dir == 2) vWalls[col + 1, row    ] = false;
            else               vWalls[col,     row    ] = false;

            Carve(nc, nr);
        }
    }

    // ─── Visual Building ─────────────────────────────────────────────────
    // All positions use anchor=(0.5,0.5) on mazeContainerRect so (0,0) = centre of container.
    // Walls use pivot=(0,0): anchoredPosition = bottom-left corner from container centre.
    // Markers/cursor use pivot=(0.5,0.5): anchoredPosition = centre from container centre.

    private void BuildMazeVisuals()
    {
        float mazeW = cols * cellSize;
        float mazeH = rows * cellSize;
        float ox    = -mazeW * 0.5f;
        float oy    = -mazeH * 0.5f;

        // Horizontal walls — bottom edge of cell (c, r)
        for (int c = 0; c < cols; c++)
            for (int r = 0; r <= rows; r++)
            {
                if (!hWalls[c, r]) continue;
                SpawnWall(ox + c * cellSize       - wallThickness * 0.5f,
                          oy + r * cellSize        - wallThickness * 0.5f,
                          cellSize + wallThickness, wallThickness);
            }

        // Vertical walls — left edge of cell (c, r)
        for (int c = 0; c <= cols; c++)
            for (int r = 0; r < rows; r++)
            {
                if (!vWalls[c, r]) continue;
                SpawnWall(ox + c * cellSize       - wallThickness * 0.5f,
                          oy + r * cellSize        - wallThickness * 0.5f,
                          wallThickness,             cellSize + wallThickness);
            }

        // Start marker — centre of bottom-left cell (0, 0)
        GameObject startGo = SpawnMarker(ox + cellSize * 0.5f,
                                         oy + cellSize * 0.5f,
                                         cellSize * 0.35f, startColor, "StartMarker");
        startMarkerRect  = startGo.GetComponent<RectTransform>();
        startMarkerImage = startGo.GetComponent<Image>();

        // End zone — centre of top-right cell (cols-1, rows-1)
        GameObject endGo = SpawnMarker(ox + (cols - 0.5f) * cellSize,
                                       oy + (rows - 0.5f) * cellSize,
                                       cellSize * 0.5f, endColor, "EndZone");
        endZoneRect = endGo.GetComponent<RectTransform>();
    }

    private void SpawnWall(float x, float y, float w, float h)
    {
        GameObject go = new GameObject("Wall", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(mazeContainerRect, false);

        RectTransform rt  = go.GetComponent<RectTransform>();
        rt.anchorMin      = new Vector2(0.5f, 0.5f);
        rt.anchorMax      = new Vector2(0.5f, 0.5f);
        rt.pivot          = new Vector2(0f,   0f);    // bottom-left pivot
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta      = new Vector2(w, h);

        Image img         = go.GetComponent<Image>();
        img.color         = wallColor;
        img.raycastTarget = false;

        go.AddComponent<MazeWall>();
        generatedObjects.Add(go);
        wallRects.Add(rt);
    }

    private GameObject SpawnMarker(float x, float y, float size, Color color, string objName)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(mazeContainerRect, false);

        RectTransform rt  = go.GetComponent<RectTransform>();
        rt.anchorMin      = new Vector2(0.5f, 0.5f);
        rt.anchorMax      = new Vector2(0.5f, 0.5f);
        rt.pivot          = new Vector2(0.5f, 0.5f); // centre pivot — anchoredPosition = centre
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta      = new Vector2(size, size);

        Image img         = go.GetComponent<Image>();
        img.color         = color;
        img.raycastTarget = false;

        generatedObjects.Add(go);
        return go;
    }

    // ─── Runtime ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (!isActive)                return;
        if (gazeDetector == null)     return;
        if (!gazeDetector.IsTracking) return;

        // Phase 1 — cursor follows head freely, player navigates to the pulsing start marker
        if (isWaitingForStart)
        {
            MoveCursor();          // cursor moves with head — player can see it
            PulseStartMarker();    // start marker pulses so player knows where to go
            CheckWaitForStart();   // activates maze once cursor overlaps start marker
            return;
        }

        // Phase 2 — normal maze play
        if (isSolving) return;

        MoveCursor();
        CheckWallCollision();
        CheckWinCondition();
    }

    // Cursor moves freely, start marker pulses — player just needs to bring cursor to it
    private void EnterWaitForStart()
    {
        isWaitingForStart = true;
        isSolving         = false;

        if (cursorImage != null)
            cursorImage.color = normalCursor;

        if (statusText != null)
            statusText.text = "MOVE CURSOR TO START";
    }

    // Pulses the start marker so the player can clearly see where to navigate to
    private void PulseStartMarker()
    {
        if (startMarkerImage == null) return;
        float t = Mathf.PingPong(Time.time * 4f, 1f);
        startMarkerImage.color = Color.Lerp(startColor, Color.white, t * 0.7f);
    }

    // Activates once the moving cursor overlaps the start marker rect
    private void CheckWaitForStart()
    {
        if (startMarkerRect == null) return;

        if (GetLocalRect(mazeCursorRect).Overlaps(GetLocalRect(startMarkerRect)))
            ActivateMaze();
    }

    // Called once cursor reaches the start marker — begins normal maze play
    private void ActivateMaze()
    {
        isWaitingForStart = false;

        // Restore start marker to its original colour
        if (startMarkerImage != null)
            startMarkerImage.color = startColor;

        if (cursorImage != null)
            cursorImage.color = normalCursor;

        if (statusText != null)
            statusText.text = "NAVIGATE TO EXIT";
    }

    private void MoveCursor()
    {
        if (mazeCamera == null) return;

        // Project head position directly onto the canvas in local space
        Vector2 rawLocal;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mazeContainerRect,
                gazeDetector.ScreenPosition,
                mazeCamera,
                out rawLocal))
            return;

        // Scale the position toward the canvas centre by cursorSensitivity.
        // 1.0 = full range (cursor reaches canvas edges when head does).
        // 0.5 = half range (cursor only reaches halfway even if head is at the screen edge).
        // This reduces how much a small head movement shifts the cursor, without any lag or drift.
        smoothedLocalPos = rawLocal * cursorSensitivity;

        // Clamp so cursor stays within the container
        float hw = mazeContainerRect.rect.width  * 0.5f;
        float hh = mazeContainerRect.rect.height * 0.5f;
        smoothedLocalPos.x = Mathf.Clamp(smoothedLocalPos.x, -hw, hw);
        smoothedLocalPos.y = Mathf.Clamp(smoothedLocalPos.y, -hh, hh);

        mazeCursorRect.anchoredPosition = smoothedLocalPos;
    }

    private void CheckWallCollision()
    {
        Rect cursorRect = GetLocalRect(mazeCursorRect);
        foreach (RectTransform wr in wallRects)
        {
            if (wr != null && GetLocalRect(wr).Overlaps(cursorRect))
            {
                OnHitWall();
                return;
            }
        }
    }

    private void CheckWinCondition()
    {
        if (endZoneRect == null) return;
        if (GetLocalRect(mazeCursorRect).Overlaps(GetLocalRect(endZoneRect)))
            OnMazeWon();
    }

    // Returns a Rect in container-local space for any direct child of mazeContainerRect.
    // Handles both pivot=(0,0) walls and pivot=(0.5,0.5) markers/cursor correctly.
    private static Rect GetLocalRect(RectTransform rt)
    {
        Vector2 pivot  = rt.pivot;
        Vector2 size   = rt.sizeDelta;
        // Compute the centre in container-local space regardless of pivot
        Vector2 centre = rt.anchoredPosition + new Vector2(size.x * (0.5f - pivot.x),
                                                            size.y * (0.5f - pivot.y));
        return new Rect(centre - size * 0.5f, size);
    }

    private void OnHitWall()
    {
        isSolving = true;
        if (cursorImage != null) cursorImage.color = hitCursor;
        if (statusText  != null) statusText.text   = "COLLISION — RESTARTING";
        StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(restartDelay);
        EnterWaitForStart();
    }

    private void OnMazeWon()
    {
        isSolving = true;
        if (cursorImage != null) cursorImage.color = winCursor;
        if (statusText  != null) statusText.text   = "ACCESS GRANTED";
        StartCoroutine(WinSequence());
    }

    private IEnumerator WinSequence()
    {
        yield return new WaitForSeconds(0.8f);

        // ── Hide the maze walls, markers and cursor ───────────────────────────
        foreach (GameObject go in generatedObjects)
            if (go != null) go.SetActive(false);

        if (mazeCursorRect != null) mazeCursorRect.gameObject.SetActive(false);

        // ── Resolve the digit ─────────────────────────────────────────────────
        // Digit slot 3 is never placed on a wall — it is always revealed here.
        // GetDigit returns -1 if the level state isn't registered yet, so we
        // fall back to a random digit to guarantee something is always shown.
        CodeNumberManager mgr = CodeNumberManager.Instance;
        if (mgr == null) mgr = FindObjectOfType<CodeNumberManager>();

        int digit = -1;
        if (mgr != null) digit = mgr.GetDigit(levelIndex, 3);
        if (digit < 0)   digit = Random.Range(0, 10);   // safe fallback

        Debug.Log($"[MazeMinigame] Win — revealing digit {digit} for level {levelIndex} slot 3");

        // ── Show the digit on the canvas ──────────────────────────────────────
        // Clear status text — the reveal label handles all messaging
        if (statusText != null) statusText.text = "";
        ShowDigitReveal(digit);

        // ── Report to CodeNumberManager — always, digit is guaranteed valid ───
        if (mgr != null)
            mgr.OnDigitCollected(levelIndex, 3, digit);

        yield return new WaitForSeconds(3.0f);

        OnMazeSolvedCallback?.Invoke();
    }

    // Spawns a large glowing digit in the centre of the canvas.
    // Copies the font from statusText so the runtime-created TMP_Text has a
    // valid font asset and actually renders (TMP_Text added via AddComponent has
    // no font by default and is invisible without one).
    private void ShowDigitReveal(int digit)
    {
        if (mazeContainerRect == null) return;

        // Grab the font asset from an existing working TMP_Text in this canvas
        TMPro.TMP_FontAsset font = (statusText != null) ? statusText.font : null;

        // ── "TERMINAL CODE :" label ───────────────────────────────────────────
        TMPro.TMP_Text labelText = SpawnRevealText(
            "RevealLabel",
            "TERMINAL CODE :",
            new Vector2(0f, 80f),
            new Vector2(500f, 60f),
            28f,
            new Color(0.2f, 1f, 0.3f, 0.85f),
            font);

        // ── Large digit ───────────────────────────────────────────────────────
        TMPro.TMP_Text digitText = SpawnRevealText(
            "RevealDigit",
            digit.ToString(),
            new Vector2(0f, -20f),
            new Vector2(300f, 220f),
            180f,
            new Color(0.15f, 1f, 0.25f, 1f),
            font);

        if (digitText != null)
        {
            digitText.fontStyle = TMPro.FontStyles.Bold;
            StartCoroutine(PulseRevealDigit(digitText));
        }
    }

    // Helper: creates one TMP_Text child of mazeContainerRect, copies font, registers for cleanup
    private TMPro.TMP_Text SpawnRevealText(string objName, string content,
                                           Vector2 anchoredPos, Vector2 size,
                                           float fontSize, Color color,
                                           TMPro.TMP_FontAsset font)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(mazeContainerRect, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;

        // Add TMP_Text AFTER setting up the RectTransform to avoid layout warnings
        TMPro.TMP_Text txt = go.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) txt.font = font;   // copy font — without this the text is invisible
        txt.text      = content;
        txt.fontSize  = fontSize;
        txt.alignment = TMPro.TextAlignmentOptions.Center;
        txt.color     = color;
        txt.raycastTarget = false;

        generatedObjects.Add(go);
        return txt;
    }

    private IEnumerator PulseRevealDigit(TMPro.TMP_Text text)
    {
        Color baseColor = new Color(0.15f, 1f,  0.25f, 1f);
        Color glowColor = new Color(0.6f,  1f,  0.7f,  1f);
        while (text != null)
        {
            float t = Mathf.PingPong(Time.time * 3f, 1f);
            text.color = Color.Lerp(baseColor, glowColor, t);
            yield return null;
        }
    }

    private void ResetCursorToStart()
    {
        // Both cursor and start marker use pivot (0.5,0.5) so anchoredPosition = centre
        if (mazeCursorRect != null)
            mazeCursorRect.anchoredPosition = startLocalPos;

        if (cursorImage != null)
            cursorImage.color = normalCursor;
    }
}
