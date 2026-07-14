using UnityEngine;

public sealed class BlocklyCubeController : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 0f;
    [SerializeField] private bool paused;
    [SerializeField] private bool aborted;

    public float RotationSpeed => rotationSpeed;
    public bool IsPaused => paused;
    public bool IsAborted => aborted;

    private void Update()
    {
        if (paused || aborted || Mathf.Approximately(rotationSpeed, 0f))
            return;

        float degreesPerSecond = rotationSpeed * 90f;
        transform.Rotate(new Vector3(0.35f, 1f, 0.15f), degreesPerSecond * Time.deltaTime, Space.World);
    }

    public void SetRotationSpeed(double speed)
    {
        rotationSpeed = Mathf.Clamp((float)speed, -20f, 20f);
        paused = false;
        aborted = false;
    }

    public void Pause()
    {
        paused = true;
    }

    public void StopRotation()
    {
        rotationSpeed = 0f;
    }

    public void Resume()
    {
        paused = false;
        aborted = false;
    }

    public void Abort()
    {
        aborted = true;
        paused = false;
        rotationSpeed = 0f;
        transform.rotation = Quaternion.identity;
    }
}
