using UnityEngine;

public class ElementPicker : MonoBehaviour
{
    public Camera targetCamera;
    public string currentText = "Toca o haz click sobre una barra.";

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Pick(Input.mousePosition);
        }

        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            Pick(Input.GetTouch(0).position);
        }
    }

    private void Pick(Vector2 screenPosition)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            ElementSelectable selectable = hit.collider.GetComponent<ElementSelectable>();
            currentText = selectable != null
                ? selectable.GetValuesAt(hit.point)
                : "Seleccionaste un objeto sin resultados.";
        }
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(20, 20, 320, 150), currentText);
    }
}
