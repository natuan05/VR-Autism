using UnityEngine;
using VRAutism.Cloud.Models;

namespace VRAutism.Core.Telemetry
{
    /// <summary>
    /// Thu thập dữ liệu cảm biến và hành vi của trẻ trong môi trường VR.
    /// Gắn Script này vào "XR Origin" hoặc một đối tượng quản lý Telemetry trong Scene bài học.
    /// </summary>
    public class SensorHarvester : MonoBehaviour
    {
        [Header("Tùy biến XR Rig")]
        [Tooltip("Transform của tay trái (Left Controller)")]
        public Transform leftHand;
        
        [Tooltip("Transform của tay phải (Right Controller)")]
        public Transform rightHand;

        [Header("Tùy biến Raycast (Điểm nhìn Focus)")]
        [Tooltip("Khoảng cách tối đa để tính là đang nhìn vào vật thể")]
        public float maxFocusDistance = 20f;
        [Tooltip("LayerMask chứa các vật thể cần đo focus (vd: Interactable)")]
        public LayerMask focusLayerMask = Physics.DefaultRaycastLayers;

        private Camera _mainCamera;
        private Vector3 _lastLeftHandPos;
        private Vector3 _lastRightHandPos;
        private float _lastSampleTime;

        private void Awake()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                Debug.LogWarning("[SensorHarvester] Không tìm thấy Camera.main! Telemetry đầu/mắt sẽ không hoạt động.");
            }
        }

        private void Start()
        {
            _lastSampleTime = Time.time;
            
            if (leftHand != null) _lastLeftHandPos = leftHand.position;
            if (rightHand != null) _lastRightHandPos = rightHand.position;
        }

        /// <summary>
        /// Tạo một bản ghi mới (Snapshot) chứa dữ liệu ngay tại khoảnh khắc được gọi.
        /// Hàm này sẽ được RealtimeDBManager gọi định kỳ mỗi 2 giây.
        /// </summary>
        public BehaviorSnapshot TakeSnapshot(float sessionTimeOffset)
        {
            float deltaTime = Time.time - _lastSampleTime;
            if (deltaTime <= 0) deltaTime = 0.001f; // Tránh chia cho 0

            // 1. Góc xoay của đầu (Yaw - trục Y)
            float headYaw = _mainCamera != null ? _mainCamera.transform.eulerAngles.y : 0f;

            // 2. Tính vận tốc tay (m/s)
            float leftVelocity = 0f;
            float rightVelocity = 0f;

            if (leftHand != null)
            {
                float distance = Vector3.Distance(leftHand.position, _lastLeftHandPos);
                leftVelocity = distance / deltaTime;
                _lastLeftHandPos = leftHand.position;
            }

            if (rightHand != null)
            {
                float distance = Vector3.Distance(rightHand.position, _lastRightHandPos);
                rightVelocity = distance / deltaTime;
                _lastRightHandPos = rightHand.position;
            }

            // Lấy vận tốc lớn hơn giữa 2 tay (đại diện cho sự vận động)
            float maxHandVelocity = Mathf.Max(leftVelocity, rightVelocity);

            // 3. Phóng tia Raycast từ mắt để lấy vật thể đang nhìn
            string focusObject = "None";
            if (_mainCamera != null)
            {
                Ray ray = new Ray(_mainCamera.transform.position, _mainCamera.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, maxFocusDistance, focusLayerMask))
                {
                    focusObject = hit.collider.gameObject.name;
                }
            }

            // Cập nhật lại thời gian snapshot
            _lastSampleTime = Time.time;

            // Đóng gói dữ liệu ra Model
            return new BehaviorSnapshot(
                timeOffset: sessionTimeOffset,
                headYaw: headYaw,
                handVel: maxHandVelocity,
                focusObj: focusObject
            );
        }
    }
}
