using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 75f;
    public float xSpeed = 120f;
    public float ySpeed = 80f;
    public float zoomSpeed = 4f;
    public float panSpeed = 8f;

    [Header("Fast navigation")]
    [Range(0.05f, 0.4f)] public float zoomPercentPerStep = 0.18f;
    public float keyboardPanSpeed = 24f;
    public float fastNavigationMultiplier = 2.5f;
    public float minDistance = 3f;
    public float maxDistance = 180f;

    private float x = 45f;
    private float y = 28f;

    private void Start()
    {
        if (target == null)
        {
            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.position = new Vector3(-0.7f, 5f, -4.5f);
            target = pivot.transform;
        }

        UpdatePosition();
    }

    private void LateUpdate()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        bool fastNavigation = keyboard != null &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        float speedMultiplier = fastNavigation ? fastNavigationMultiplier : 1f;

        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            x += delta.x * xSpeed * Time.deltaTime * speedMultiplier;
            y -= delta.y * ySpeed * Time.deltaTime * speedMultiplier;
            y = Mathf.Clamp(y, -10f, 80f);
        }

        // Pan con el boton central (arrastrar para moverse lateralmente).
        if (mouse != null && mouse.middleButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            Vector2 pan = -delta * panSpeed * Time.deltaTime * speedMultiplier;
            Pan(pan.x, pan.y);
        }

        // Pan con flechas o WASD. Shift activa el desplazamiento rapido.
        if (keyboard != null)
        {
            float ax = 0f;
            float ay = 0f;
            if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) ax += 1f;
            if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) ax -= 1f;
            if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) ay += 1f;
            if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) ay -= 1f;
            if (ax != 0f || ay != 0f)
            {
                Vector2 direction = new Vector2(ax, ay).normalized;
                Pan(direction.x * keyboardPanSpeed * speedMultiplier * Time.deltaTime,
                    direction.y * keyboardPanSpeed * speedMultiplier * Time.deltaTime);
            }
        }

        float scroll = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f;
#else
        bool fastNavigation = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speedMultiplier = fastNavigation ? fastNavigationMultiplier : 1f;

        if (Input.GetMouseButton(1))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * Time.deltaTime * speedMultiplier;
            y -= Input.GetAxis("Mouse Y") * ySpeed * Time.deltaTime * speedMultiplier;
            y = Mathf.Clamp(y, -10f, 80f);
        }

        if (Input.GetMouseButton(2))
        {
            Vector2 pan = -new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            Pan(pan.x * panSpeed * speedMultiplier, pan.y * panSpeed * speedMultiplier);
        }

        float ax = ((Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) ? 1f : 0f) -
            ((Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) ? 1f : 0f);
        float ay = ((Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) ? 1f : 0f) -
            ((Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) ? 1f : 0f);
        if (ax != 0f || ay != 0f)
        {
            Vector2 direction = new Vector2(ax, ay).normalized;
            Pan(direction.x * keyboardPanSpeed * speedMultiplier * Time.deltaTime,
                direction.y * keyboardPanSpeed * speedMultiplier * Time.deltaTime);
        }

        float rawScroll = Input.GetAxis("Mouse ScrollWheel");
        float scroll = Mathf.Abs(rawScroll) > 0.0001f ? Mathf.Sign(rawScroll) : 0f;
#endif
        if (scroll != 0f && SelectedBeamDiagramPanel.BlocksPointer()) scroll = 0f;
        ApplyZoom(scroll, speedMultiplier);

        UpdatePosition();
    }

    private void Pan(float screenX, float screenY)
    {
        Quaternion rotation = Quaternion.Euler(y, x, 0f);
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;

        Vector3 offset = right * screenX + up * screenY;
        target.position += offset;
    }

    private void UpdatePosition()
    {
        Quaternion rotation = Quaternion.Euler(y, x, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
        transform.position = target.position + offset;
        transform.rotation = rotation;
    }

    private void ApplyZoom(float scrollSteps, float speedMultiplier)
    {
        if (Mathf.Abs(scrollSteps) < 0.0001f) return;

        // El paso depende de la distancia actual: rapido en vista general y preciso
        // al acercarse. zoomSpeed conserva compatibilidad con el valor de la escena.
        float legacyAdjustment = Mathf.Max(0.25f, zoomSpeed / 4f);
        float fraction = Mathf.Clamp(zoomPercentPerStep * legacyAdjustment * speedMultiplier, 0.04f, 0.45f);
        distance *= Mathf.Pow(1f - fraction, scrollSteps);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    public void FocusOn(Vector3 point, float newDistance = -1f)
    {
        if (target == null)
        {
            GameObject pivot = new GameObject("CameraPivot");
            target = pivot.transform;
        }
        target.position = point;
        if (newDistance > 0f)
        {
            distance = Mathf.Clamp(newDistance, minDistance, maxDistance);
        }
        UpdatePosition();
    }

    public void SetPreset(string preset)
    {
        if (preset == "TOP") { x = 0f; y = 80f; }
        else if (preset == "FRONT") { x = 0f; y = 8f; }
        else if (preset == "RIGHT") { x = 90f; y = 8f; }
        else if (preset == "ISO") { x = 45f; y = 30f; }
        UpdatePosition();
    }
}
