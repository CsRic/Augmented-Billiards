// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static PassthroughCameraSamples.MultiObjectDetection.SentisInferenceUiManager;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;

        [Header("Controls configuration")]
        [SerializeField] private OVRInput.RawButton m_actionButton = OVRInput.RawButton.A;

        [Header("Ui references")]
        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;

        [Header("Placement configureation")]
        [SerializeField] private GameObject m_spwanMarker; // virtual ball
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private float m_spawnDistance = 0.05f;
        [SerializeField] private AudioSource m_placeSound = null;

        [Header("Sentis inference ref")]
        [SerializeField] private SentisInferenceRunManager m_runInference;
        [SerializeField] private SentisInferenceUiManager m_uiInference;
        [SerializeField] private AnchorManager m_anchorManager = null;
        [Header("Auto‑spawn settings")]
        [SerializeField] private float m_markerLife = 2f;

        [Space(10)]
        public UnityEvent<int> OnObjectsIdentified;
        [Header("Ray debug draw")]
        [SerializeField] private Material m_rayMaterial;
        [SerializeField] private float m_rayWidth = 0.05f;
        [SerializeField] private float m_rayDefaultLen = 0.5f;
        private const float nearOffset = 0.1f;
        private readonly List<GameObject> m_rayPool = new();
        private bool m_isPaused = true;
        private List<GameObject> m_spwanedEntities = new();
        private bool m_isStarted = false;
        private bool m_isSentisReady = false;
        private float m_delayPauseBackTime = 0;
        private Vector3 m_headPosAtInferenceStart = Vector3.zero;
        private Vector3 m_headForAtInferenceStart = Vector3.zero;

        #region Unity Functions

        private void Awake()
        {
            OVRManager.display.RecenteredPose += CleanMarkersCallBack;

            m_runInference.OnInferenceResultsReady += HandleInferenceResultsReady;
        }

        private void OnDestroy()
        {
            m_runInference.OnInferenceResultsReady -= HandleInferenceResultsReady;
        }

        /// <summary>
        /// Sentis 推理结果就绪 → 立即尝试生成 / 更新 Marker
        /// </summary>
        private void HandleInferenceResultsReady()
        {
            if (m_isPaused) return;                     // Respect Pause
            if (m_anchorManager == null) return;
            if (!m_anchorManager.IsStable()) return;

            var bounds = m_anchorManager.getBounds();
            SpwanCurrentDetectedObjectsAuto(bounds);
        }
        private class MarkerInfo
        {
            public GameObject go;
            public string className;
            public float lastSeenTime;
        }
        private readonly List<MarkerInfo> m_activeMarkers = new();
        public struct BallInfoNormalized
        {
            public string ClassName;   // 例如 "8‑ball" / "red" / "cue"
            public Vector2 UV;          // 归一化桌面坐标 (u∈[0,1], v∈[0,ratio])
        }
        public struct TableState
        {
            public Vector2 TableSize; // (1 , ratio)，ratio = 短/长
            public List<BallInfoNormalized> Balls;
        }
        private bool IsInsideFov(Vector3 worldPos)
        {
            var dir = (worldPos - m_headPosAtInferenceStart).normalized;
            return Vector3.Angle(m_headForAtInferenceStart, dir) <= 45f;
        }
        private void SpawnOrRefreshMarker(Vector3 hitPos, string className)
        {
            List<MarkerInfo> same_targets = new();
            foreach (var m in m_activeMarkers)
            {
                if (Vector3.Distance(m.go.transform.position, hitPos) <= m_spawnDistance)
                {
                    same_targets.Add(m);
                }
            }

            if (same_targets.Count > 0)
            {
                // same object, delete the old one(s)
                for(int i = same_targets.Count - 1; i >= 0; --i)
                {
                    var m = same_targets[i];
                    same_targets.RemoveAt(i);
                    Destroy(m.go);
                    m_activeMarkers.Remove(m);
                }
            }
            else
            {
                // make a sound for the new object
                if (m_placeSound != null) m_placeSound.Play();
            }

            var g = Instantiate(m_spwanMarker);
            g.transform.SetPositionAndRotation(hitPos, Quaternion.identity);
            // g.GetComponent<DetectionSpawnMarkerAnim>().SetYoloClassName(className);

            m_activeMarkers.Add(new MarkerInfo
            {
                go = g,
                className = className,
                lastSeenTime = Time.time
            });
        }
        private void CullExpiredMarkers()
        {
            float now = Time.time;
            for (int i = m_activeMarkers.Count - 1; i >= 0; --i)
            {
                var m = m_activeMarkers[i];
                if (!IsInsideFov(m.go.transform.position))
                {
                    m.lastSeenTime = now;
                    continue;
                }
                if (now - m.lastSeenTime > m_markerLife)
                {
                    Destroy(m.go);
                    m_activeMarkers.RemoveAt(i);
                }
            }
        }
        private IEnumerator Start()
        {
            // Wait until Sentis model is loaded
            var sentisInference = FindAnyObjectByType<SentisInferenceRunManager>();
            while (!sentisInference.IsModelLoaded)
            {
                yield return null;
            }
            m_isSentisReady = true;
            m_delayPauseBackTime = Time.time;
        }

        private void Update()
        {
            // Get the WebCamTexture CPU image
            var hasWebCamTextureData = m_webCamTextureManager.WebCamTexture != null;

            if (!m_isStarted)
            {
                // Manage the Initial Ui Menu
                if (hasWebCamTextureData && m_isSentisReady)
                {
                    m_uiMenuManager.OnInitialMenu(m_environmentRaycast.HasScenePermission());
                    m_isStarted = true;
                }
            }
            else
            {
                // Modification: Auto detection
                // update per 0.5 seconds
                // if (Time.time - m_delayPauseBackTime > 0.5f)
                // {
                //     m_delayPauseBackTime = Time.time;
                //     if (m_anchorManager != null && m_anchorManager.IsStable())
                //     {
                //         List<Vector3> bounds = m_anchorManager.getBounds();
                //         SpwanCurrentDetectedObjectsAuto(bounds);
                //     }
                // }

            }

            // Run a new inference when the current inference finishes
            if (!m_runInference.IsRunning())
            {
                m_runInference.RunInference(m_webCamTextureManager.WebCamTexture);
                m_headPosAtInferenceStart = Camera.main.transform.position;
                m_headForAtInferenceStart = Camera.main.transform.forward;
            }
        }
        #endregion

        #region Marker Functions
        /// <summary>
        /// Clean 3d markers when the tracking space is re-centered.
        /// </summary>
        private void CleanMarkersCallBack()
        {
            foreach (var e in m_spwanedEntities)
            {
                Destroy(e, 0.1f);
            }
            m_spwanedEntities.Clear();
            OnObjectsIdentified?.Invoke(-1);
        }
        private void SpwanCurrentDetectedObjectsAuto(List<Vector3> bounds)
        {
            if (bounds.Count < 4) return;
            ClearDebugRays();
            var p0 = bounds[0];
            var p1 = bounds[1];
            var p2 = bounds[2];
            var p3 = bounds[3];

            int newCount = 0;
            foreach (var box in m_uiInference.BoxDrawn)
            {
                // Generate a ray
                if (!box.WorldPos.HasValue) continue;
                Vector3 box_pos = box.WorldPos.Value;
                Ray ray = new Ray(m_headPosAtInferenceStart, (box_pos - m_headPosAtInferenceStart).normalized);

                float rayLen = m_rayDefaultLen;
                if (RayIntersectsTriangle(ray, p0, p1, p2, out Vector3 hit) ||
                    RayIntersectsTriangle(ray, p0, p2, p3, out hit))
                {
                    if (!IsInsideFov(hit)) continue;
                    rayLen = Vector3.Distance(ray.origin, hit);
                    SpawnOrRefreshMarker(hit, box.ClassName);
                    ++newCount;
                }
                DrawDebugRay(ray, rayLen);
            }
            CullExpiredMarkers();
            // if (newCount > 0 && m_placeSound != null) m_placeSound.Play();
            OnObjectsIdentified?.Invoke(newCount);
        }

        private bool RayIntersectsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);
            if (Mathf.Abs(a) < 1e-6)
                return false; // Parallel

            float f = 1.0f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0.0 || u > 1.0)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0.0 || u + v > 1.0)
                return false;

            float t = f * Vector3.Dot(edge2, q);
            if (t > 1e-6)
            {
                hitPoint = ray.origin + ray.direction * t;
                return true;
            }
            return false;
        }

        private void ClearDebugRays()
        {
            foreach (var go in m_rayPool) Destroy(go);
            m_rayPool.Clear();
        }

        private void DrawDebugRay(Ray ray, float length)
        {
            Vector3 p0 = ray.origin + ray.direction * nearOffset;
            Vector3 p1 = p0 + ray.direction * (length - nearOffset);

            var go = new GameObject("DebugRay");
            var lr = go.AddComponent<LineRenderer>();

            lr.positionCount = 2;
            lr.SetPosition(0, p0);
            lr.SetPosition(1, p1);

            lr.startWidth = lr.endWidth = m_rayWidth;
            lr.useWorldSpace = true;

            if (m_rayMaterial != null)
                lr.material = m_rayMaterial;
            else
            {
                lr.material = new Material(Shader.Find("Unlit/Color")) { color = Color.cyan };
                lr.material.enableInstancing = true;
            }

            m_rayPool.Add(go);
        }

        #endregion

        #region Public Functions
        /// <summary>
        /// Pause the detection logic when the pause menu is active
        /// </summary>
        public void OnPause(bool pause)
        {
            m_isPaused = pause;
        }
        #endregion

        /// <summary>
        /// 计算“当前局面”：把活动球心映射到归一化桌面坐标系并返回。
        /// </summary>
        /// <param name="bounds">AnchorManager 提供的四个桌面顶点，顺/逆时针即可</param>
        public TableState? GetCurrentTableState(List<Vector3> bounds)
        {
            if (bounds == null || bounds.Count != 4)
                return null;

            // 1) 收集球心世界坐标
            var ballWorldPos = new List<Vector3>(m_activeMarkers.Count);
            foreach (var m in m_activeMarkers)
                ballWorldPos.Add(m.go.transform.position);

            // 2) 调用前面写好的工具函数完成投影与归一化
            var mapRes = TableCoordinateUtil.MapBallsToTable(bounds, ballWorldPos);

            if (mapRes == null)
                return null;
            // 3) 结合类别名拼装结果
            var balls = new List<BallInfoNormalized>(m_activeMarkers.Count);
            for (int i = 0; i < m_activeMarkers.Count; ++i)
            {
                balls.Add(new BallInfoNormalized
                {
                    ClassName = m_activeMarkers[i].className,
                    UV = mapRes.Value.BallUVs[i]
                });
            }

            return new TableState
            {
                TableSize = mapRes.Value.TableSize,
                Balls = balls
            };
        }
    }

    public static class TableCoordinateUtil
    {
        public struct TableMappingResult
        {
            public Vector2 TableSize;       // (1, ratio)
            public List<Vector2> BallUVs;   // 每个球的 (u,v) 归一化坐标
        }

        /// <summary>
        /// 将台面 <paramref name="bounds"/> 和球心 <paramref name="ballWorldPos"/>
        /// 映射到归一化矩形坐标系。
        /// </summary>
        /// <remarks>
        /// bounds 需按顺时针 / 逆时针给出四个顶点 (p0‑p1‑p2‑p3)。  
        /// 返回值 TableSize.x == 1，TableSize.y == ratio (短 / 长)。  
        /// BallUVs 中 u ∈ [0,1]，v ∈ [0,ratio]。
        /// </remarks>
        public static TableMappingResult? MapBallsToTable(
                List<Vector3> bounds,
                List<Vector3> ballWorldPos)
        {
            if (bounds == null || bounds.Count != 4)
                return null;

            // —— 1. 取 p0 为原点，p0→p1、p0→p3 为两条相邻边 ——
            Vector3 p0 = bounds[0];
            Vector3 edgeA = bounds[1] - p0;   // p0→p1
            Vector3 edgeB = bounds[3] - p0;   // p0→p3

            // —— 2. 保证 edgeLong 为较长边 ——
            Vector3 edgeLong, edgeShort;
            if (edgeA.sqrMagnitude >= edgeB.sqrMagnitude)
            {
                edgeLong = edgeA;
                edgeShort = edgeB;
            }
            else
            {
                edgeLong = edgeB;
                edgeShort = edgeA;
            }

            float lenLong = edgeLong.magnitude;
            float lenShort = edgeShort.magnitude;
            float ratio = lenShort / lenLong;          // < 1

            // —— 3. 预计算求解系数矩阵 (edgeLong, edgeShort)⁻¹ ——
            //      delta = u*edgeLong + v*edgeShort → 求 (u,v)
            float a11 = Vector3.Dot(edgeLong, edgeLong);
            float a12 = Vector3.Dot(edgeLong, edgeShort);
            float a22 = Vector3.Dot(edgeShort, edgeShort);
            float det = a11 * a22 - a12 * a12;
            if (Mathf.Abs(det) < 1e-6f)
                return null; // 线性方程组无解

            Vector3 n = Vector3.Cross(edgeLong, edgeShort).normalized; // 平面法向量

            // —— 4. 处理每个球 ——
            List<Vector2> uvList = new(ballWorldPos.Count);
            foreach (var pos in ballWorldPos)
            {
                // 4.1 投影到桌面平面
                Vector3 delta = pos - p0;
                float distN = Vector3.Dot(delta, n);
                Vector3 proj = delta - distN * n;

                // 4.2 线性求解 (u,v)
                float b1 = Vector3.Dot(proj, edgeLong);
                float b2 = Vector3.Dot(proj, edgeShort);

                float u = (b1 * a22 - b2 * a12) / det;   // 真实长度坐标
                float v = (a11 * b2 - a12 * b1) / det;

                // 4.3 归一化：长边映射到 0‑1
                float uNorm = u / lenLong;
                float vNorm = v / lenLong;                // 注意除以 lenLong

                uvList.Add(new Vector2(uNorm, vNorm));
            }

            return new TableMappingResult
            {
                TableSize = new Vector2(1f, ratio),
                BallUVs = uvList
            };
        }
    }
}
