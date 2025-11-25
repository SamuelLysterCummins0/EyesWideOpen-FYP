using UnityEngine;

public class footsteps : MonoBehaviour
{
    public AudioClip[] walkingSounds;
    public AudioSource footstepsSound;
    private int walkIndex = 0;

    void Update()
    {
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D))
        {
            PlayWalkingSound();
        }
        else
        {
            footstepsSound.Stop();
        }
    }

    void PlayWalkingSound()
    {
        if (!footstepsSound.isPlaying)
        {
            footstepsSound.clip = walkingSounds[walkIndex];
            footstepsSound.Play();
            walkIndex = (walkIndex + 1) % walkingSounds.Length;
        }
    }
}
