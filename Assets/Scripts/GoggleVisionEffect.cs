using UnityEngine;

/// <summary>
/// Attached to the main camera at runtime by GoggleController.
/// Applies the GoggleVision shader (greyscale + purple tint + contrast boost)
/// over the full screen whenever the component is enabled.
///
/// GoggleController.UseGoggles() enables / disables this component.
/// The shader is found by name ("Custom/GoggleVision") so no manual assignment
/// is required — just make sure the GoggleVision.shader file is in the project.
/// </summary>
[RequireComponent(typeof(Camera))]
public class GoggleVisionEffect : MonoBehaviour
{
    private Material effectMaterial;

    // ── Unity ────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (effectMaterial == null)
            BuildMaterial();
    }

    private void OnDisable()
    {
        // Destroy the temporary material so it doesn't leak between play sessions
        if (effectMaterial != null)
        {
            DestroyImmediate(effectMaterial);
            effectMaterial = null;
        }
    }

    // ── Image Effect ─────────────────────────────────────────────────────────

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (effectMaterial != null)
            Graphics.Blit(source, destination, effectMaterial);
        else
            Graphics.Blit(source, destination);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BuildMaterial()
    {
        Shader shader = Shader.Find("Custom/GoggleVision");
        if (shader == null)
        {
            Debug.LogError("[GoggleVisionEffect] Cannot find shader 'Custom/GoggleVision'. " +
                           "Make sure GoggleVision.shader is in the project under Assets/Shaders/.");
            return;
        }
        effectMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }
}
