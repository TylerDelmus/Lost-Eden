using UnityEngine;

public class Attractor : MonoBehaviour
{
    const float GizmoRadius = 0.04f;

    public AttractorPlace Place;

    void OnDrawGizmos()
    {
        DrawGizmo(selected: false);
    }

    void OnDrawGizmosSelected()
    {
        DrawGizmo(selected: true);
    }

    void DrawGizmo(bool selected)
    {
        Color color = selected ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.2f, 0.85f, 1f, 0.9f);
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, GizmoRadius);

        float axis = GizmoRadius * 2.5f;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + transform.right * axis);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + transform.up * axis);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * axis);
    }
}
