using UnityEngine;

public class HeartbeatManager : MonoBehaviour
{
    public static HeartbeatManager Instance;

    public AudioClip heartbeatAudioClip; // Reference to your heartbeat audio clip
    public float minHeartbeatVolume = 0.1f;
    public float maxHeartbeatVolume = 1f;
    public float minHeartbeatPitch = 0.8f;
    public float maxHeartbeatPitch = 1.5f;
    public float heartbeatDistanceThreshold = 10f;

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
        heartbeatAudio.clip = heartbeatAudioClip; // Assign your heartbeat audio clip
        heartbeatAudio.loop = true;
        heartbeatAudio.volume = minHeartbeatVolume;
        heartbeatAudio.pitch = minHeartbeatPitch;
        heartbeatAudio.Play();
    }

    public void UpdateHeartbeat(float distanceToPlayer)
    {
        float t = Mathf.Clamp01(1f - (distanceToPlayer / heartbeatDistanceThreshold));

        heartbeatAudio.volume = Mathf.Lerp(minHeartbeatVolume, maxHeartbeatVolume, t);
        heartbeatAudio.pitch = Mathf.Lerp(minHeartbeatPitch, maxHeartbeatPitch, t);
    }

    public void ResetHeartbeat()
{
    heartbeatAudio.volume = minHeartbeatVolume;
    heartbeatAudio.pitch = minHeartbeatPitch;
}
}