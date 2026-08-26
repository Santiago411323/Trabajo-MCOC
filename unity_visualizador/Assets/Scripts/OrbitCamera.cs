using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 10f;
    public float xSpeed = 120f;
    public float ySpeed = 80f;
    public float zoomSpeed = 4f;

    private float x = 45f;
    private float y = 28f;

    private void Start()
    {
        if (target == null)
        {
            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.position = new Vector3(3f, 1.6f, 2.5f);
            target = pivot.transform;
        }

        UpdatePosition();
    }

    private void LateUpdate()
    {
        if (Input.GetMouseButton(1))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * Time.deltaTime;
            y -= Input.GetAxis("Mouse Y") * ySpeed * Time.deltaTime;
            y = Mathf.Clamp(y, -10f, 80f);
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        distance = Mathf.Clamp(distance - scroll * zoomSpeed, 3f, 25f);

        UpdatePosition();
    }

    private void UpdatePosition()
    {
        Quaternion rotation = Quaternion.Euler(y, x, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
        transform.position = target.position + offset;
        transform.rotation = rotation;
    }
}
