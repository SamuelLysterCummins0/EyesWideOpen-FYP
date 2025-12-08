using UnityEngine;

public class footsteps : MonoBehaviour
{
    public AudioClip[] walkingSounds; 
    public AudioClip[] sprintingSounds; 
    public AudioSource footstepsSound;
    public AudioSource sprintSound;
    private int walkIndex = 0; 
    private int sprintIndex = 0; 

    void Update()
    {
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D))
        {
            if (Input.GetKey(KeyCode.LeftShift))
            {
                PlaySprintingSound();
            }
            else
            {
                PlayWalkingSound();
            }
        }
        else
        {
            footstepsSound.Stop();
            sprintSound.Stop();
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

    void PlaySprintingSound()
    {
        if (!sprintSound.isPlaying)
        {
            sprintSound.clip = sprintingSounds[sprintIndex];
            sprintSound.Play();
            sprintIndex = (sprintIndex + 1) % sprintingSounds.Length; 
        }
    }
}
