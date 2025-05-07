// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using System.IO;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif


namespace PassthroughCameraSamples.CameraToWorld
{
    [MetaCodeSample("PassthroughCameraApiSamples-CameraToWorld-Simple")]
    public class CameraToWorldManagerSimple : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;
        private Vector2Int CameraResolution => m_webCamTextureManager.RequestedResolution;
        [SerializeField] private GameObject m_centerEyeAnchor;

        [SerializeField] private Text m_debugText;

        [SerializeField] private CameraToWorldCameraCanvas m_cameraCanvas;
        [SerializeField] private float m_canvasDistance = 1f;

        [SerializeField] private Vector3 m_headSpaceDebugShift = new(0, -.15f, .4f);

        private bool m_isDebugOn;
        private bool m_snapshotTaken;
        private OVRPose m_snapshotHeadPose;

        private void Awake() => OVRManager.display.RecenteredPose += RecenterCallBack;

        private IEnumerator Start()
        {
            if (m_webCamTextureManager == null)
            {
                Debug.LogError($"PCA: {nameof(m_webCamTextureManager)} field is required "
                            + $"for the component {nameof(CameraToWorldManager)} to operate properly");
                enabled = false;
                yield break;
            }

            // Make sure the manager is disabled in scene and enable it only when the required permissions have been granted
            Assert.IsFalse(m_webCamTextureManager.enabled);
            while (PassthroughCameraPermissions.HasCameraPermission != true)
            {
                yield return null;
            }

            // Set the 'requestedResolution' and enable the manager
            m_webCamTextureManager.RequestedResolution = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye).Resolution;
            m_webCamTextureManager.enabled = true;

            ScaleCameraCanvas();
        }

        private void Update()
        {
            if (m_webCamTextureManager.WebCamTexture == null)
                return;

            // 添加：监听右控制器扳机按键
            if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
            {
                // 开启协程捕捉当前帧并保存截图
                StartCoroutine(CaptureScreenshot());
            }

            if (!m_snapshotTaken)
            {
                UpdateMarkerPoses();

                if (m_isDebugOn)
                {
                    // Move the updated markers forward to better see them
                    TranslateMarkersForDebug(moveForward: true);
                }
            }
        }

        /// <summary>
        /// Calculate the dimensions of the canvas based on the distance from the camera origin and the camera resolution
        /// </summary>
        private void ScaleCameraCanvas()
        {
            var cameraCanvasRectTransform = m_cameraCanvas.GetComponentInChildren<RectTransform>();
            var leftSidePointInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(CameraEye, new Vector2Int(0, CameraResolution.y / 2));
            var rightSidePointInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(CameraEye, new Vector2Int(CameraResolution.x, CameraResolution.y / 2));
            var horizontalFoVDegrees = Vector3.Angle(leftSidePointInCamera.direction, rightSidePointInCamera.direction);
            var horizontalFoVRadians = horizontalFoVDegrees / 180 * Math.PI;
            var newCanvasWidthInMeters = 2 * m_canvasDistance * Math.Tan(horizontalFoVRadians / 2);
            var localScale = (float)(newCanvasWidthInMeters / cameraCanvasRectTransform.sizeDelta.x);
            cameraCanvasRectTransform.localScale = new Vector3(localScale, localScale, localScale);
        }

        private void UpdateMarkerPoses()
        {
            var headPose = OVRPlugin.GetNodePoseStateImmediate(OVRPlugin.Node.Head).Pose.ToOVRPose();

            var cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(CameraEye);

            // Position the canvas in front of the camera
            m_cameraCanvas.transform.position = cameraPose.position + cameraPose.rotation * Vector3.forward * m_canvasDistance;
            m_cameraCanvas.transform.rotation = cameraPose.rotation;

        }

        private void TranslateMarkersForDebug(bool moveForward)
        {
            var gameObjects = new[]
            {
                m_cameraCanvas.gameObject
            };

            var direction = m_snapshotTaken ? m_snapshotHeadPose.orientation : m_centerEyeAnchor.transform.rotation;

            foreach (var go in gameObjects)
            {
                go.transform.position += direction * m_headSpaceDebugShift * (moveForward ? 1 : -1);
            }
        }

        private void RecenterCallBack()
        {
            if (m_snapshotTaken)
            {
                m_snapshotTaken = false;
                m_webCamTextureManager.WebCamTexture.Play();
                m_cameraCanvas.ResumeStreamingFromCamera();
                m_snapshotHeadPose = OVRPose.identity;
            }
        }

        /// <summary>
        /// 捕获当前画面并保存为.jpg图片，文件名使用当前时间命名
        /// </summary>
        /// <returns></returns>
        private IEnumerator CaptureScreenshot()
        {
            // 确保已调用 MakeCameraSnapshot() 更新 m_cameraCanvas 内的快照数据
            m_cameraCanvas.MakeCameraSnapshot();

            // 等待一帧，确保快照制作完成
            yield return new WaitForEndOfFrame();

            // 直接从 m_cameraCanvas 获取快照 Texture2D（确保 CameraToWorldCameraCanvas 中有相应的 public GetSnapshot() 方法）
            Texture2D snapshot = m_cameraCanvas.GetSnapshot();
            if (snapshot == null)
            {
                m_debugText.text = "failed to get snapshot: snapshot is null";
                yield break;
            }

            byte[] imageBytes = snapshot.EncodeToJPG();

            // 使用当前日期时间作为文件名，例如：20250411_154530.jpg
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = timestamp + ".jpg";

            // 获取公共保存路径，Android 平台优先保存到 Pictures 目录
            string filePath = GetPublicSavePath(filename);
            try
            {
                File.WriteAllBytes(filePath, imageBytes);
                m_debugText.text = "snapshot saved: " + filePath;
                // 扫描文件，让图库能识别更新后的媒体数据
                ScanFileInGallery(filePath);
            }
            catch (Exception ex)
            {
                m_debugText.text = "snapshot saving failed: " + ex.Message;
            }

            m_cameraCanvas.ResumeStreamingFromCamera();
        }

        private string GetPublicSavePath(string filename)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
    using (AndroidJavaClass envClass = new AndroidJavaClass("android.os.Environment"))
    {
        // 可选：选择 DIRECTORY_PICTURES 或 DIRECTORY_DCIM 等公共目录
        string publicDir = envClass.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", 
                                envClass.GetStatic<string>("DIRECTORY_PICTURES"))
                                .Call<string>("getAbsolutePath");
        return Path.Combine(publicDir, filename);
    }
#else
            // 对于非 Android 平台，仍保存到 persistentDataPath
            return Path.Combine(Application.persistentDataPath, filename);
#endif
        }

        private void ScanFileInGallery(string filePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
    using (AndroidJavaClass playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
    {
        AndroidJavaObject activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity");
        using (AndroidJavaClass mediaScannerConnection = new AndroidJavaClass("android.media.MediaScannerConnection"))
        {
            mediaScannerConnection.CallStatic("scanFile", activity, new string[] { filePath }, null, null);
        }
    }
#endif
        }

    }
}
