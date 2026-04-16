using System.Collections;
using UnityEngine;

/// <summary>
/// Attach this to the jumpscare room NPC — the purely visual Pacer that plays
/// the jumpscare animation. It has no AI and no collider.
/// Called externally by PacerNPC when the wandering Pacer catches the player.
///
/// ─── SETUP ───────────────────────────────────────────────────────────────────
/// 1. Attach this script to the jumpscare room NPC GameObject.
/// 2. Assign the Animator field to the Animator on this NPC.
/// 3. Assign the Jumpscare Clip (the audio sting).
/// 4. Leave Jumpscare Anim Trigger as "Jumpscare" unless your parameter is named
///    differently in the Animator Controller.
/// 5. CameraControl is found automatically at runtime; no manual wiring needed.
/// </summary>
public class PacerJumpscare : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Animator on this NPC that contains the jumpscare animation clip.")]
    [SerializeField] private Animator jumpscareAnimator;

    [Header("Jumpscare")]
    [Tooltip("Sound that plays when the Pacer catches the player.")]
    [SerializeField] private AudioClip jumpscareClip;
    [Tooltip("Name of the Trigger parameter in the Animator Controller. Must match exactly.")]
    [SerializeField] private string jumpscareAnimTrigger = "Jumpscare";

    // ── Private state ──────────────────────────────────────────────────────────

    private AudioSource   _audioSource;
    private CameraControl _cameraControl;
    private bool          _isInJumpscare;
    private Vector3       _lockedPosition;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _audioSource              = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 1f;
        _audioSource.maxDistance  = 20f;
        _audioSource.playOnAwake  = false;
    }

    private void Start()
    {
        _cameraControl  = FindObjectOfType<CameraControl>();
        _isInJumpscare  = false;

        // Lock to wherever the NPC is placed in the scene — never moves from this point.
        _lockedPosition = transform.position;

        // Clear any default-set trigger so the animation doesn't play on spawn.
        if (jumpscareAnimator != null && !string.IsNullOrEmpty(jumpscareAnimTrigger))
            jumpscareAnimator.ResetTrigger(jumpscareAnimTrigger);
    }

    // ── Public API — called by PacerNPC when the player is caught ──────────────

    public void TriggerJumpscare()
    {
        if (_isInJumpscare) return;

        _isInJumpscare  = true;
        _lockedPosition = transform.position;

        if (jumpscareClip != null)
            _audioSource.PlayOneShot(jumpscareClip);

        if (jumpscareAnimator != null && !string.IsNullOrEmpty(jumpscareAnimTrigger))
        {
            jumpscareAnimator.ResetTrigger(jumpscareAnimTrigger);
            jumpscareAnimator.SetTrigger(jumpscareAnimTrigger);
        }
        else
        {
            Debug.LogWarning($"[PacerJumpscare] Cannot fire trigger — animator is {(jumpscareAnimator == null ? "NULL" : "assigned")}, trigger name is '{jumpscareAnimTrigger}'");
        }

        if (_cameraControl != null)
            _cameraControl.TriggerPacerJumpscare();

        StartCoroutine(ResetAfterJumpscare());
    }

    private IEnumerator ResetAfterJumpscare()
    {
        yield return new WaitForSeconds(5f);
        _isInJumpscare = false;
    }

    // Always enforce the scene-placed position after the Animator runs.
    // Prevents gravity, root motion, or idle animation keyframes from
    // sinking or shifting the NPC at any point.
    private void LateUpdate()
    {
        transform.position = _lockedPosition;
    }
}
