#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class FullscreenHotkeyHandler : MonoBehaviour
{
    void Update()
    {
        if (!Application.isPlaying)
            return;

        // Toggle fullscreen with P
        if (Input.GetKeyDown(KeyCode.P))
        {
            FullscreenGameView.Toggle();
        }
    }
}

public static class FullscreenGameView
{
    static readonly Type GameViewType =
        Type.GetType("UnityEditor.GameView,UnityEditor");

    static readonly PropertyInfo ShowToolbarProperty =
        GameViewType.GetProperty(
            "showToolbar",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    static EditorWindow instance;

    static FullscreenGameView()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ForceClose;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.quitting += ForceClose;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            ForceClose();
    }

    static void ForceClose()
    {
        if (instance != null)
        {
            instance.Close();
            instance = null;
        }
    }

    // Ctrl/Cmd + P menu fallback (works even if input focus breaks)
    [MenuItem("Window/General/Game (Fullscreen) %p")]
    public static void Toggle()
    {
        if (!Application.isPlaying)
            return;

        if (instance != null)
        {
            ForceClose();
            return;
        }

        instance = (EditorWindow)ScriptableObject.CreateInstance(GameViewType);
        ShowToolbarProperty?.SetValue(instance, false);

        var res = Screen.currentResolution;
        instance.ShowPopup();
        instance.position = new Rect(0, 0, res.width, res.height);
        instance.Focus();
    }
}
#endif
