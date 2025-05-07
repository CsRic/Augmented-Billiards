using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Draws a line projected onto the plane defined by AnchorManager:
/// ‑ start is 1.5 m behind the foot point (optionally rotated around end),
/// ‑ end is 0.5 m in front of the foot point (total 2 m length).
/// Public API: GetStickStatus() → (end position, normalized forward direction).
/// </summary>
public class VirtualStick : MonoBehaviour
{
    [Header("References")]
    public AnchorManager anchorManager;   // Drag your AnchorManager here
    public Material     lineMaterial;     // Shared line material

    [Header("Lengths (m)")]
    public float frontLen  = 0.5f;        // Foot → forward
    public float backLen   = 1.5f;        // Foot → backward

    [Header("Width / Angle")]
    public float lineWidth      = 0.01f;  // Line width
    public float startRotateDeg = 15f;    // Positive = CCW around plane normal

    private LineRenderer dirLine;

    // Cached for external queries
    private Vector3 lastEnd = Vector3.zero;
    private Vector3 lastDir = Vector3.forward;

    // ───────────────────────────────────────────────
    void Awake()
    {
        dirLine = CreateLineRenderer("DirLine", Color.red);
        dirLine.positionCount = 2;
    }

    void Update()
    {
        var bounds = anchorManager?.getBounds();
        if (bounds == null || !TryGetPlane(bounds, out Vector3 n, out Vector3 center))
        {
            dirLine.enabled = false;
            return;
        }

        Vector3 headPos = Camera.main.transform.position;
        Vector3 headDir = Camera.main.transform.forward;

        Vector3 foot   = ProjectPointOnPlane(headPos, center, n); // Point on plane
        Vector3 dirOnP = ProjectVectorOnPlane(headDir, n);
        if (dirOnP.sqrMagnitude < 1e-6f) { dirLine.enabled = false; return; }

        dirOnP.Normalize();

        // Initial start / end before rotation
        Vector3 start = foot - dirOnP * backLen;
        Vector3 end   = foot + dirOnP * frontLen;

        // Rotate start around end if requested
        if (Mathf.Abs(startRotateDeg) > 0.01f)
        {
            Vector3 v = start - end; // Vector from end to start
            start = end + Quaternion.AngleAxis(startRotateDeg, n) * v;
        }

        // Update LineRenderer
        dirLine.enabled = true;
        dirLine.SetPosition(0, start);
        dirLine.SetPosition(1, end);

        // Cache for GetStickStatus
        lastEnd = end;
        Vector3 segDir = end - start;
        lastDir = segDir.sqrMagnitude > 1e-6f ? segDir.normalized : dirOnP;
    }

    // ──────────── Public API ────────────
    /// <summary>
    /// Returns the end‑point position and normalized forward direction
    /// (the direction in which the line continues beyond end).
    /// </summary>
    public (Vector3 endPoint, Vector3 forwardDir) GetStickStatus()
        => (lastEnd, lastDir);

    // ──────────── Helpers ────────────
    LineRenderer CreateLineRenderer(string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.material      = lineMaterial;
        lr.startColor    = lr.endColor = col;
        lr.startWidth    = lr.endWidth = lineWidth;
        lr.useWorldSpace = true;
        return lr;
    }

    static Vector3 ProjectPointOnPlane(Vector3 p, Vector3 p0, Vector3 n)
        => p - Vector3.Dot(p - p0, n) * n;

    static Vector3 ProjectVectorOnPlane(Vector3 v, Vector3 n)
        => v - Vector3.Dot(v, n) * n;

    static bool TryGetPlane(List<Vector3> pts, out Vector3 n, out Vector3 center)
    {
        if (pts.Count < 3) { n = Vector3.up; center = Vector3.zero; return false; }
        n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
        center = Vector3.zero;
        foreach (var p in pts) center += p;
        center /= pts.Count;
        return n.sqrMagnitude > 1e-6f;
    }
}
