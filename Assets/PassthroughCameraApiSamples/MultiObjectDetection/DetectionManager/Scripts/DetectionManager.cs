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
    // ─────────────────────────────────────────────────────────────────────────
    //  DetectionManager
    // ─────────────────────────────────────────────────────────────────────────
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;

        [Header("Controls configuration")]
        [SerializeField] private OVRInput.RawButton m_actionButton = OVRInput.RawButton.A;

        [Header("UI references")]
        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;

        [Header("Placement configuration")]
        [SerializeField] private GameObject m_spwanMarker;
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private float m_spawnDistance = 0.05f;
        [SerializeField] private AudioSource m_placeSound = null;

        [Header("Sentis inference reference")]
        [SerializeField] private SentisInferenceRunManager m_runInference;
        [SerializeField] private SentisInferenceUiManager  m_uiInference;
        [SerializeField] public AnchorManager             m_anchorManager = null;
        [SerializeField] private VirtualStick              m_virtualStick  = null;

        [Header("Auto‑spawn settings")]
        [SerializeField] private float m_markerLife = 2f;

        [Space(10)]
        public UnityEvent<int> OnObjectsIdentified;

        [Header("Ray debug draw")]
        [SerializeField] private Material m_rayMaterial;
        [SerializeField] private float    m_rayWidth      = 0.05f;
        [SerializeField] private float    m_rayDefaultLen = 0.5f;

        // debug rendering of restored world coords
        [Header("State debug (restored world objects)")]
        [SerializeField] private float    m_debugPointScale = 0.03f;
        [SerializeField] private Material m_debugRedMat     = null;

        private const float nearOffset = 0.1f;

        private readonly List<GameObject> m_rayPool        = new();
        private readonly List<GameObject> m_stateDebugObjs = new();

        private bool   m_isPaused      = true;
        private bool   m_isStarted     = false;
        private bool   m_isSentisReady = false;
        private float  m_delayPauseBackTime = 0;
        private Vector3 m_headPosAtInferenceStart = Vector3.zero;
        private Vector3 m_headForAtInferenceStart = Vector3.zero;

        private LineRenderer m_stickLine = null;
        private List<LineRenderer> m_predictLines = new();
        private float ballRealRadius = 0.02858f;

        // inner data ----------------------------------------------------------
        private class MarkerInfo
        {
            public GameObject go;
            public string className;
            public float lastSeenTime;
        }
        private readonly List<MarkerInfo> m_activeMarkers = new();

        public struct BallInfoNormalized
        {
            public string  ClassName;
            public Vector2 UV;
        }
        public struct TableState
        {
            public Vector2 TableSize;
            public List<BallInfoNormalized> Balls;
            public Vector2 StickPos;
            public Vector2 StickDir;
            public float ballRadiusRatio;
        }

        // ───────────────────────── Unity lifecycle ──────────────────────────
        private void Awake()
        {
            m_runInference.OnInferenceResultsReady += HandleInferenceResultsReady;
        }
        private void OnDestroy()
        {
            m_runInference.OnInferenceResultsReady -= HandleInferenceResultsReady;
        }
        private IEnumerator Start()
        {
            var sentis = FindAnyObjectByType<SentisInferenceRunManager>();
            while (!sentis.IsModelLoaded) yield return null;
            m_isSentisReady      = true;
            m_delayPauseBackTime = Time.time;
        }
        private void Update()
        {
            bool hasCam = m_webCamTextureManager.WebCamTexture != null;
            if (!m_isStarted && hasCam && m_isSentisReady)
            {
                m_uiMenuManager.OnInitialMenu(m_environmentRaycast.HasScenePermission());
                m_isStarted = true;
            }

            if (!m_runInference.IsRunning())
            {
                m_runInference.RunInference(m_webCamTextureManager.WebCamTexture);
                m_headPosAtInferenceStart = Camera.main.transform.position;
                m_headForAtInferenceStart = Camera.main.transform.forward;
            }
        }

        // ───────────────────────── inference callback ───────────────────────
        private void HandleInferenceResultsReady()
        {
            if (m_isPaused) return;
            if (m_anchorManager == null || !m_anchorManager.IsStable()) return;
            SpwanCurrentDetectedObjectsAuto(m_anchorManager.getBounds());
        }

        // ───────────────────────── marker utilities ─────────────────────────
        private bool IsInsideFov(Vector3 worldPos)
        {
            var dir = (worldPos - m_headPosAtInferenceStart).normalized;
            return Vector3.Angle(m_headForAtInferenceStart, dir) <= 45f;
        }
        private void SpawnOrRefreshMarker(Vector3 hitPos, string className)
        {
            List<MarkerInfo> same = new();
            foreach (var m in m_activeMarkers)
                if (Vector3.Distance(m.go.transform.position, hitPos) <= m_spawnDistance)
                    same.Add(m);

            if (same.Count > 0)
            {
                foreach (var m in same)
                {
                    Destroy(m.go);
                    m_activeMarkers.Remove(m);
                }
            }
            else if (m_placeSound != null) m_placeSound.Play();

            var g = Instantiate(m_spwanMarker);
            g.transform.SetPositionAndRotation(hitPos, Quaternion.identity);
            m_activeMarkers.Add(new MarkerInfo
            {
                go = g, className = className, lastSeenTime = Time.time
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

        // ───────────────────────── ray debug helpers ────────────────────────
        private void ClearDebugRays()  { foreach (var g in m_rayPool) Destroy(g); m_rayPool.Clear(); }
        private void DrawDebugRay(Ray ray, float len)
        {
            Vector3 p0 = ray.origin + ray.direction * nearOffset;
            Vector3 p1 = p0 + ray.direction * (len - nearOffset);

            var go = new GameObject("DebugRay");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, p0);
            lr.SetPosition(1, p1);
            lr.startWidth = lr.endWidth = m_rayWidth;
            lr.useWorldSpace = true;
            lr.material = m_rayMaterial ?? new Material(Shader.Find("Unlit/Color")){color=Color.cyan};
            m_rayPool.Add(go);
        }

        // ───────────────────────── marker flow ──────────────────────────────
        private void SpwanCurrentDetectedObjectsAuto(List<Vector3> b)
        {
            if (b.Count < 4) return;
            ClearDebugRays();

            int newCnt = 0;
            foreach (var box in m_uiInference.BoxDrawn)
            {
                if (!box.WorldPos.HasValue) continue;
                Vector3 boxPos = box.WorldPos.Value;
                Ray ray = new Ray(m_headPosAtInferenceStart, (boxPos - m_headPosAtInferenceStart).normalized);

                float len = m_rayDefaultLen;
                if (HitTable(ray, b, out Vector3 hit))
                {
                    if (!IsInsideFov(hit)) continue;
                    len = Vector3.Distance(ray.origin, hit);
                    SpawnOrRefreshMarker(hit, box.ClassName);
                    ++newCnt;
                }
                DrawDebugRay(ray, len);
            }
            CullExpiredMarkers();
            OnObjectsIdentified?.Invoke(newCnt);
        }
        private static bool HitTable(Ray r, List<Vector3> b, out Vector3 hit)
        {
            bool A = RayIntersectsTriangle(r, b[0], b[1], b[2], out hit);
            if (A) return true;
            return RayIntersectsTriangle(r, b[0], b[2], b[3], out hit);
        }
        private static bool RayIntersectsTriangle(
            Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            Vector3 e1 = v1 - v0, e2 = v2 - v0, h = Vector3.Cross(ray.direction, e2);
            float a = Vector3.Dot(e1, h);
            if (Mathf.Abs(a) < 1e-6f) return false;
            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0f || u + v > 1f) return false;
            float t = f * Vector3.Dot(e2, q);
            if (t > 1e-6f) { hitPoint = ray.origin + ray.direction * t; return true; }
            return false;
        }

        // ───────────────────────── public API ───────────────────────────────
        public void OnPause(bool pause) => m_isPaused = pause;

        public TableState? GetCurrentTableState()
        {
            var bounds = m_anchorManager.getBounds();
            if (bounds == null || bounds.Count != 4) return null;

            var ballsPos = new List<Vector3>(m_activeMarkers.Count);
            foreach (var m in m_activeMarkers) ballsPos.Add(m.go.transform.position);

            (Vector3 stickPos, Vector3 stickDir) = m_virtualStick.GetStickStatus();
            var res = TableCoordinateUtil.MapBallsToTable(bounds, ballsPos, stickPos, stickDir);
            if (res == null) return null;

            var balls = new List<BallInfoNormalized>(m_activeMarkers.Count);
            for (int i = 0; i < m_activeMarkers.Count; ++i)
                balls.Add(new BallInfoNormalized { ClassName = m_activeMarkers[i].className,
                                                   UV = res.Value.BallUVs[i] });
            TableCoordinateUtil.TryBuildBasis(bounds, out TableCoordinateUtil.TableBasis tb);
            float ballRadiusRatio = ballRealRadius / tb.lenLong;
            return new TableState
            {
                TableSize = res.Value.TableSize,
                Balls = balls,
                StickPos = res.Value.StickPos,
                StickDir = res.Value.StickDir,
                ballRadiusRatio = ballRadiusRatio
            };
        }

        public void RenderNormalizedState(TableState state, List<List<Vector2>> predictedTrajectories)
        {
            // clear previous
            foreach (var g in m_stateDebugObjs) Destroy(g);
            m_stateDebugObjs.Clear();
            if (m_stickLine != null) Destroy(m_stickLine.gameObject);
            foreach (var g in m_predictLines) Destroy(g.gameObject);
            m_predictLines.Clear();

            if (!TableCoordinateUtil.TryBuildBasis(m_anchorManager.getBounds(),
                                                   out TableCoordinateUtil.TableBasis tb))
                return;

            Vector3 W(Vector2 uv) => tb.UVToWorld(uv);
            Vector3 D(Vector2 dirUv) => tb.DirUVToWorld(dirUv).normalized;

            // balls
            foreach (var b in state.Balls)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                g.transform.position = W(b.UV);
                g.transform.localScale = Vector3.one * m_debugPointScale;
                if (m_debugRedMat != null)
                    g.GetComponent<Renderer>().material = m_debugRedMat;
                m_stateDebugObjs.Add(g);
            }
            // stick point
            var sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sp.transform.position = W(state.StickPos);
            sp.transform.localScale = Vector3.one * m_debugPointScale;
            if (m_debugRedMat != null)
                sp.GetComponent<Renderer>().material = m_debugRedMat;
            m_stateDebugObjs.Add(sp);

            // stick dir line
            Vector3 dirW = D(state.StickDir);
            float len = 0.25f;
            m_stickLine = new GameObject("StickDirLine").AddComponent<LineRenderer>();
            m_stickLine.positionCount = 2;
            m_stickLine.SetPosition(0, sp.transform.position);
            m_stickLine.SetPosition(1, sp.transform.position + dirW * len);
            m_stickLine.startWidth = m_stickLine.endWidth = 0.01f;
            m_stickLine.useWorldSpace = true;
            if (m_debugRedMat != null) m_stickLine.material = m_debugRedMat;

            // basis
            // var eL = new GameObject("EdgeLong").AddComponent<LineRenderer>();
            // eL.positionCount = 2;
            // eL.SetPosition(0, tb.origin);
            // eL.SetPosition(1, tb.origin + tb.edgeLong);
            // eL.startWidth = eL.endWidth = 0.01f;
            // eL.useWorldSpace = true;
            // if (m_debugRedMat != null) eL.material = m_debugRedMat;
            // m_predictLines.Add(eL);
            // var eS = new GameObject("EdgeShort").AddComponent<LineRenderer>();
            // eS.positionCount = 2;
            // eS.SetPosition(0, tb.origin);
            // eS.SetPosition(1, tb.origin + tb.edgeShort);
            // eS.startWidth = eS.endWidth = 0.01f;
            // eS.useWorldSpace = true;
            // if (m_debugRedMat != null) eS.material = m_debugRedMat;
            // m_predictLines.Add(eS);

            // predicted trajectories
            foreach (var pred in predictedTrajectories)
            {
                var g = new GameObject("PredictedTrajectory");
                var lr = g.AddComponent<LineRenderer>();
                lr.positionCount = pred.Count;
                for (int i = 0; i < pred.Count; ++i)
                    lr.SetPosition(i, W(pred[i]));
                lr.startWidth = lr.endWidth = 0.01f;
                lr.useWorldSpace = true;
                if (m_debugRedMat != null) lr.material = m_debugRedMat;
                m_predictLines.Add(lr);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TableCoordinateUtil
    // ─────────────────────────────────────────────────────────────────────────
    public static class TableCoordinateUtil
    {
        // basis struct -------------------------------------------------------
        public readonly struct TableBasis
        {
            public readonly Vector3 origin;
            public readonly Vector3 edgeLong;
            public readonly Vector3 edgeShort;
            public readonly float   lenLong;
            public readonly float   ratio;

            internal TableBasis(Vector3 p0, Vector3 longE, Vector3 shortE, float shortRatio)
            {
                origin    = p0;
                edgeLong  = longE; // longE;
                edgeShort = shortE; // length same as longE, but direction is different
                lenLong   = longE.magnitude;
                ratio = shortRatio;
            }
            public Vector3 UVToWorld(Vector2 uv)
                => origin + edgeLong * uv.x + edgeShort * uv.y;
            public Vector3 DirUVToWorld(Vector2 dirUv)
                => edgeLong * dirUv.x + edgeShort * dirUv.y;
        }

        public static bool TryBuildBasis(List<Vector3> bounds, out TableBasis basis)
        {
            basis = default;
            if (bounds == null || bounds.Count != 4) return false;

            Vector3 p0 = bounds[0];
            Vector3 eA = bounds[1] - p0;
            Vector3 eB = bounds[3] - p0;

            Vector3 eLong  = eA.sqrMagnitude >= eB.sqrMagnitude ? eA : eB;
            Vector3 eShort = eA.sqrMagnitude >= eB.sqrMagnitude ? eB : eA;

            if (eLong.sqrMagnitude < 1e-6f || eShort.sqrMagnitude < 1e-6f)
                return false;

            float shortRatio = eShort.magnitude / eLong.magnitude;
            basis = new TableBasis(p0, eLong, eShort/shortRatio, shortRatio);
            return true;
        }

        // mapping result -----------------------------------------------------
        public struct TableMappingResult
        {
            public Vector2 TableSize;
            public List<Vector2> BallUVs;
            public Vector2 StickPos;
            public Vector2 StickDir;
        }

        // main mapping -------------------------------------------------------
        public static TableMappingResult? MapBallsToTable(
            List<Vector3> bounds,
            List<Vector3> balls,
            Vector3 stickPos,
            Vector3 stickDir)
        {
            if (!TryBuildBasis(bounds, out TableBasis tb)) return null;

            Vector3 el = tb.edgeLong, es = tb.edgeShort;
            float a11 = Vector3.Dot(el, el);
            float a12 = Vector3.Dot(el, es);
            float a22 = Vector3.Dot(es, es);
            float det = a11 * a22 - a12 * a12;
            if (Mathf.Abs(det) < 1e-6f) return null;

            Vector3 n = Vector3.Cross(el, es).normalized;

            Vector2 W2U(Vector3 w)
            {
                Vector3 d = w - tb.origin;
                Vector3 p = d - Vector3.Dot(d, n) * n;
                float b1 = Vector3.Dot(p, el);
                float b2 = Vector3.Dot(p, es);
                float u = (b1 * a22 - b2 * a12) / det;
                float v = (a11 * b2 - a12 * b1) / det;
                return new Vector2(u, v);
            }

            var uvBalls = new List<Vector2>(balls.Count);
            foreach (var w in balls) uvBalls.Add(W2U(w));

            Vector2 stickPosUv = W2U(stickPos);

            Vector3 dirProj = stickDir - Vector3.Dot(stickDir, n) * n;
            Vector2 stickDirUv;
            if (dirProj.sqrMagnitude < 1e-8f)
            {
                stickDirUv = Vector2.right;
            }
            else
            {
                float b1d = Vector3.Dot(dirProj, el);
                float b2d = Vector3.Dot(dirProj, es);
                float uD = (b1d * a22 - b2d * a12) / det;
                float vD = (a11 * b2d - a12 * b1d) / det;
                stickDirUv = new Vector2(uD, vD).normalized;
            }

            return new TableMappingResult
            {
                TableSize = new Vector2(1f, tb.ratio),
                BallUVs   = uvBalls,
                StickPos  = stickPosUv,
                StickDir  = stickDirUv
            };
        }
    }
}
