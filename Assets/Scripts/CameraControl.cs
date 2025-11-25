using UnityEngine;
using System.Collections;
using SUPERCharacter;

public class CameraControl : MonoBehaviour
{
    public float jumpscareHoldTime = 5f;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private bool isInJumpscare = false;
    private bool isReturningCamera = false;
    private Transform playerCameraTransform;
private Transform originalParent;
    public SUPERCharacterAIO playerController;
    public Transform jumpscareCamera;
    public AudioSource jumpscareAudio;

    void Start()
    {
        if (playerController == null)
        {
            playerController = FindObjectOfType<SUPERCharacterAIO>();
        }

        playerCameraTransform = Camera.main.transform;
        originalParent = playerCameraTransform.parent;
        UpdateOriginalTransform();
    }

    public void UpdateOriginalTransform()
    {
        originalPosition = transform.position;
        originalRotation = transform.rotation;
    }

    public void TriggerJumpscare()
    {
        if (!isInJumpscare && !isReturningCamera)
        {
            StopAllCoroutines();
            StartCoroutine(PerformJumpscare());
        }
    }

    private IEnumerator PerformJumpscare()
    {
        isInJumpscare = true;

        if (playerController != null)
        {
            playerController.enabled = false;
        }

playerCameraTransform.parent = null;
        transform.position = jumpscareCamera.position;
        transform.rotation = jumpscareCamera.rotation;

        if (jumpscareAudio != null)
        {
            jumpscareAudio.Play();
        }

        yield return new WaitForSeconds(jumpscareHoldTime);

        yield return StartCoroutine(ReturnCameraToOriginal());
    }

    private IEnumerator ReturnCameraToOriginal()
    {
        isReturningCamera = true;

        transform.position = playerCameraTransform.position;
        transform.rotation = playerCameraTransform.rotation;

        playerCameraTransform.parent = originalParent;

        isInJumpscare = false;
        isReturningCamera = false;

        if (playerController != null)
        {
            playerController.enabled = true;
        }

        UpdateOriginalTransform();
        yield return null;
    }

    public void ResetCameraState()
    {
        StopAllCoroutines();
        isInJumpscare = false;
        isReturningCamera = false;

        transform.position = originalPosition;
        transform.rotation = originalRotation;

        // Reattach the camera to its original parent
        playerCameraTransform.parent = originalParent;
        UpdateOriginalTransform();
    }
}