// GenericPassthroughCameraUtils.cs
// Copyright (c) 2025 rui chen

using UnityEngine;

namespace PassthroughCameraSamples
{
    /// <summary>
    /// 最小化、无状态的通用相机工具类。
    /// 不依赖 OVRPlugin / AndroidJavaObject，
    /// 需要调用方显式传入相机外参 (Pose) 与内参 (Intrinsics)。
    /// </summary>
    public static class GenericPassthroughCameraUtils
    {
        #region Public API --------------------------------------------------

        /// <summary>
        /// 将像素坐标反投影到相机坐标系 (右手系, +Z 朝前) 的射线。
        /// </summary>
        public static Ray PixelToRayInCamera(
            Vector2Int pixel,
            in PassthroughCameraIntrinsics intr)
        {
            Vector3 dirCam = new(
                (pixel.x - intr.PrincipalPoint.x) / intr.FocalLength.x,
                (pixel.y - intr.PrincipalPoint.y) / intr.FocalLength.y,
                1f);

            dirCam.Normalize();
            return new Ray(Vector3.zero, dirCam);
        }

        /// <summary>
        /// 将像素坐标反投影到世界坐标系射线。
        /// </summary>
        public static Ray PixelToRayInWorld(
            Vector2Int pixel,
            in PassthroughCameraIntrinsics intr,
            in Pose cameraPoseWorld)
        {
            // 先到相机坐标
            Ray camRay = PixelToRayInCamera(pixel, intr);

            // 再用外参变换到世界坐标
            Vector3 originW = cameraPoseWorld.position;
            Vector3 dirW    = cameraPoseWorld.rotation * camRay.direction;
            return new Ray(originW, dirW.normalized);
        }

        /// <summary>
        /// 如果你更喜欢直接传 4×4 矩阵 (Unity: 列主序)：
        /// </summary>
        public static Ray PixelToRayInWorld(
            Vector2Int pixel,
            in PassthroughCameraIntrinsics intr,
            in Matrix4x4 camToWorld)
        {
            Ray camRay = PixelToRayInCamera(pixel, intr);

            Vector3 originW = camToWorld.GetColumn(3);
            Vector3 dirW    = camToWorld.MultiplyVector(camRay.direction);
            return new Ray(originW, dirW.normalized);
        }

        /// <summary>
        /// 由头部姿态与 “相机相对头部” 位姿计算世界中的相机姿态。
        /// 调用方可在捕获帧时保存这两个 Pose，再离线使用。
        /// </summary>
        public static Pose ComposeCameraPose(
            in Pose headPoseWorld,
            in Pose cameraPoseHead)
        {
            // worldFromCamera = worldFromHead * headFromCamera
            Vector3 posWorld = headPoseWorld.position + 
                               headPoseWorld.rotation * cameraPoseHead.position;
            Quaternion rotWorld = headPoseWorld.rotation * cameraPoseHead.rotation;
            return new Pose(posWorld, rotWorld);
        }

        #endregion
    }

    /// <summary>
    /// 与 Meta 官方定义保持一致的 Intrinsics 结构体。
    /// </summary>
    // public struct PassthroughCameraIntrinsics
    // {
    //     public Vector2 FocalLength;     // 像素
    //     public Vector2 PrincipalPoint;  // 像素
    //     public Vector2Int Resolution;   // 像素
    //     public float Skew;              // 一般为 0，若使用请自行处理
    // }
}
