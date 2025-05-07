using PassthroughCameraSamples.MultiObjectDetection; // access DetectionManager
using UnityEngine;

public class Prediction : MonoBehaviour
{
    [Header("Reference to DetectionManager in scene")]
    public DetectionManager detectionManager;

    void Update()
    {
        if (detectionManager == null) return;

        // get current normalized table state
        var state = detectionManager.GetCurrentTableState();
        if (state == null) return; // nothing yet

        // TODO: prediction

        // visualize it back in world space
        detectionManager.RenderNormalizedState(state.Value);


    }
}
