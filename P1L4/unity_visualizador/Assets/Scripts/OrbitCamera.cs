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
    [Range(0.05f, 0.4f)] public float zoomPercentPerStep = 0.24f;
    public float keyboardPanSpeed = 32f;
    public float fastNavigationMultiplier = 3f;
    public float minDistance = 1.5f;
    public float maxDistance = 250f;
    private StructureViewer guiViewer;

    private float x = 45f;
    private float y = 28f;
    private bool radarFocus;
    private Vector3 radarFocusStart,radarFocusEnd;
    private float radarDistanceStart,radarDistanceEnd,radarFocusElapsed;

    public void BeginRadarFocus(Vector3 point,float newDistance)
    {
        if(target==null)FocusOn(point,newDistance);
        radarFocusStart=target.position;radarFocusEnd=point;
        radarDistanceStart=distance;radarDistanceEnd=Mathf.Clamp(newDistance,minDistance,maxDistance);
        radarFocusElapsed=0;radarFocus=true;
    }
    public void CancelRadarFocus(){radarFocus=false;}

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
        if(radarFocus)
        {
            radarFocusElapsed+=Time.unscaledDeltaTime;
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(radarFocusElapsed/.85f));
            FocusOn(Vector3.Lerp(radarFocusStart,radarFocusEnd,t),Mathf.Lerp(radarDistanceStart,radarDistanceEnd,t));
            if(t>=1)radarFocus=false;
            return;
        }
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

        float rawScroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
        float scroll = NormalizeWheelSteps(rawScroll);
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
        if(scroll!=0f)
        {
            if(guiViewer==null)guiViewer=FindFirstObjectByType<StructureViewer>();
#if ENABLE_INPUT_SYSTEM
            Vector2 pointer=Mouse.current!=null?Mouse.current.position.ReadValue():Vector2.zero;
#else
            Vector2 pointer=Input.mousePosition;
#endif
            pointer.y=Screen.height-pointer.y;
            if(guiViewer!=null && ((guiViewer.IsLeftPanelVisible() && guiViewer.GetLeftPanelRect().Contains(pointer)) ||
                (guiViewer.IsTopBarVisible() && guiViewer.GetTopBarRect().Contains(pointer))))scroll=0;
            if(ElementResultsPanel.BlocksPointer(pointer))scroll=0;
        }
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

    private static float NormalizeWheelSteps(float rawScroll)
    {
        if (Mathf.Abs(rawScroll) < 0.0001f) return 0f;

        // Windows/Input System can report one wheel notch as either 120 units
        // or a small normalized value, depending on the mouse driver.  Always
        // treat a non-zero wheel event as at least one full navigation step.
        float steps = Mathf.Max(1f, Mathf.Abs(rawScroll) / 120f);
        return Mathf.Sign(rawScroll) * steps;
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
