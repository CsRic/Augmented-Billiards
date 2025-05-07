using UnityEngine;
using System.Collections.Generic;

public class AnchorManager : MonoBehaviour
{
    public GameObject anchorPrefab;
    public Material lineMaterial;

    private List<GameObject> anchors = new List<GameObject>();     // 只记录手动添加的点
    private GameObject autoAnchor;                                 // 自动推算出的第四个点
    private List<GameObject> lines = new List<GameObject>();       // 所有线条
    private GameObject previewAnchor;
    private bool stable = false; // 是否稳定

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            if (anchors.Count < 3)
                AddAnchor(GetRightControllerForwardOffsetPosition());  // 使用朝前5cm位置
        }

        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            UndoAnchor();
        }

        UpdatePreview();
    }

    public List<Vector3> getBounds()
    {
        if (!stable) return null;
        List<Vector3> bounds = new List<Vector3>();
        foreach (var anchor in anchors)
        {
            bounds.Add(anchor.transform.position);
        }
        bounds.Add(autoAnchor.transform.position);
        return bounds;
    }

    public bool IsStable()
    {
        return stable;
    }
    void AddAnchor(Vector3 position)
    {
        var anchor = Instantiate(anchorPrefab, position, Quaternion.identity);
        anchors.Add(anchor);

        if (anchors.Count == 3)
        {
            Vector3 p0 = anchors[0].transform.position;
            Vector3 p1 = anchors[1].transform.position;
            Vector3 p2 = anchors[2].transform.position;
            Vector3 p3 = p2 + (p0 - p1);  // 平行四边形推算
            autoAnchor = Instantiate(anchorPrefab, p3, Quaternion.identity);
        }
    }

    void UndoAnchor()
    {
        stable = false;
        if (anchors.Count > 0)
        {
            Destroy(anchors[^1]);
            anchors.RemoveAt(anchors.Count - 1);
        }
        Clear(autoAnchor);
    }

    void UpdatePreview()
    {
        if (!stable)
        {
            ClearAll(lines);
            if (anchors.Count == 1)
            {
                DrawLine(anchors[0].transform.position, GetRightControllerForwardOffsetPosition());
            }
            else if (anchors.Count == 2)
            {
                Vector3 p0 = anchors[0].transform.position;
                Vector3 p1 = anchors[1].transform.position;
                Vector3 p2 = GetRightControllerForwardOffsetPosition();
                Vector3 p3 = p2 + (p0 - p1);

                DrawLine(p0, p1);
                DrawLine(p1, p2);
                DrawLine(p2, p3);
                DrawLine(p3, p0);
            }
            else if (anchors.Count == 3)
            {
                Vector3 p0 = anchors[0].transform.position;
                Vector3 p1 = anchors[1].transform.position;
                Vector3 p2 = anchors[2].transform.position;
                Vector3 p3 = autoAnchor.transform.position;

                DrawLine(p0, p1);
                DrawLine(p1, p2);
                DrawLine(p2, p3);
                DrawLine(p3, p0);
                stable = true;
            }
        }

        {
            Vector3 pos = GetRightControllerForwardOffsetPosition(); // 新位置
            Quaternion rot = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);

            if (anchors.Count <= 2)
            {
                if (previewAnchor == null)
                {
                    previewAnchor = Instantiate(anchorPrefab, pos, rot);
                    var previewRenderer = previewAnchor.GetComponent<Renderer>();
                    if (previewRenderer != null)
                        previewRenderer.material.color = new Color(0, 1, 1, 0.5f); // 浅蓝半透明
                }
                else
                {
                    previewAnchor.transform.SetPositionAndRotation(pos, rot);
                }
            }
            else
            {
                //delete preview anchor if it exists
                if (previewAnchor != null)
                {
                    Destroy(previewAnchor);
                    previewAnchor = null;
                }
            }
        }
    }

    Vector3 GetRightControllerForwardOffsetPosition()
    {
        Vector3 pos = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion rot = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        return pos + rot * Vector3.forward * 0.05f;
    }

    void DrawLine(Vector3 start, Vector3 end)
    {
        var lr = CreateLine(start, end, 0.03f, Color.white);
        lines.Add(lr.gameObject);
    }

    LineRenderer CreateLine(Vector3 start, Vector3 end, float width, Color color)
    {
        GameObject go = new GameObject("Line");
        var lr = go.AddComponent<LineRenderer>();
        lr.material = lineMaterial;
        lr.positionCount = 2;
        lr.SetPositions(new[] { start, end });
        lr.startWidth = lr.endWidth = width;
        lr.startColor = lr.endColor = color;
        lr.useWorldSpace = true;
        return lr;
    }

    void Clear(GameObject obj)
    {
        if (obj != null) Destroy(obj);
    }

    void ClearAll(List<GameObject> list)
    {
        foreach (var go in list)
            Destroy(go);
        list.Clear();
    }
}
