using UnityEngine;

public class InfoSelectable : MonoBehaviour
{
    public string info;

    public virtual string GetInfo()
    {
        return string.IsNullOrEmpty(info) ? "Objeto sin informacion." : info;
    }

    public virtual void OnSelected() { }
    public virtual void OnDeselected() { }
}
