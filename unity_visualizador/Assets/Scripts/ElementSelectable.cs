using UnityEngine;

public class ElementSelectable : MonoBehaviour
{
    public ElementData data;
    public Vector3 startPoint;
    public Vector3 endPoint;

    public string GetValuesAt(Vector3 hitPoint)
    {
        Vector3 axis = endPoint - startPoint;
        float t = Vector3.Dot(hitPoint - startPoint, axis) / axis.sqrMagnitude;
        t = Mathf.Clamp01(t);

        float axial = Mathf.Lerp(data.axialI, data.axialJ, t);
        float shear = Mathf.Lerp(data.shearI, data.shearJ, t);
        float moment = Mathf.Lerp(data.momentI, data.momentJ, t);

        return $"Elemento {data.id} ({data.type})\n" +
               $"Posicion: {t * 100f:0}%\n" +
               $"Axial N: {axial:0.###} kN\n" +
               $"Corte Vz: {shear:0.###} kN\n" +
               $"Momento My: {moment:0.###} kN*m";
    }
}
