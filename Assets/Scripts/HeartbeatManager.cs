using UnityEngine;

public class HeartbeatManager : MonoBehaviour
{
    public static HeartbeatManager Instance;

    public AudioClip heartbeatAudioClip;
    public float heartbeatVolume = 0.3f;

    private AudioSource heartbeatAudio;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        heartbeatAudio = gameObject.AddComponent<AudioSource>();
        heartbeatAudio.clip = heartbeatAudioClip;
        heartbeatAudio.loop = true;
        heartbeatAudio.volume = heartbeatVolume;
        heartbeatAudio.Play();
    }

    public void UpdateHeartbeat(float distanceToPlayer)
    {
        // Just keep playing at constant volume for now
    }

    public void ResetHeartbeat()
    {
        // Nothing to reset yet
    }
}
