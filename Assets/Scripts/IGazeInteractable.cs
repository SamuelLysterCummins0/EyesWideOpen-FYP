using UnityEngine;

public interface IGazeInteractable
{
    /// <summary>
    /// How long the player needs to look at this object to activate it
    /// </summary>
    float RequiredDwellTime { get; }

    /// <summary>
    /// Called when gaze first enters this object
    /// </summary>
    void OnGazeEnter();

    /// <summary>
    /// Called every frame while being gazed at
    /// </summary>
    /// <param name="progress">0 to 1, how close to activation</param>
    void OnGazeStay(float progress);

    /// <summary>
    /// Called when gaze leaves this object
    /// </summary>
    void OnGazeExit();

    /// <summary>
    /// Called when dwell time is complete - activate the interaction
    /// </summary>
    void OnGazeActivate();
}