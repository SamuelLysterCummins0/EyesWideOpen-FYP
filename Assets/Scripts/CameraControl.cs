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
    public Transform pacerJumpscareCamera;
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
            StartCoroutine(PerformJumpscare(jumpscareCamera));
        }
    }

    public void TriggerPacerJumpscare()
    {
        if (!isInJumpscare && !isReturningCamera)
        {
            StopAllCoroutines();
            StartCoroutine(PerformJumpscare(pacerJumpscareCamera));
        }
    }

    private IEnumerator PerformJumpscare(Transform jumpscareTarget)
    {
        isInJumpscare = true;

        if (playerController != null)
        {
            playerController.enabled = false;
        }

        playerCameraTransform.parent = null;
        transform.position = jumpscareTarget.position;
        transform.rotation = jumpscareTarget.rotation;

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
        Vector3 targetPosition = playerCameraTransform.position;
        Quaternion targetRotation = playerCameraTransform.rotation;

        float elapsedTime = 0f;
        float returnDuration = 0.5f;

        while (elapsedTime < returnDuration)
        {
            elapsedTime += Time.deltaTime;
            float percentage = elapsedTime / returnDuration;
            float smoothPercentage = percentage * percentage * (3f - 2f * percentage);

            transform.position = Vector3.Lerp(transform.position, targetPosition, smoothPercentage);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, smoothPercentage);

            yield return null;
        }

        transform.position = targetPosition;
        transform.rotation = targetRotation;

 playerCameraTransform.parent = originalParent;

        isInJumpscare = false;
        isReturningCamera = false;

        if (!GameManager.Instance.IsDeathScreenActive() && playerController != null)
        {
            playerController.enabled = true;
        }

        UpdateOriginalTransform();
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