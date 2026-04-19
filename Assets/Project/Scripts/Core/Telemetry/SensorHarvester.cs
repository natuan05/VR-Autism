using System.Collections.Generic;
using UnityEngine;
using VRAutism.Core.Models;
using VRAutism.Gameplay.Actions;

namespace VRAutism.Core.Telemetry
{
    /// <summary>
    /// Thu thập dữ liệu cảm biến và hành vi của trẻ trong môi trường VR.
    /// Ghi mẫu liên tục qua FixedUpdate vào bộ đệm và tổng hợp 2 giây 1 lần.
    /// </summary>
    public class SensorHarvester : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════
        //  INSPECTOR FIELDS
        // ══════════════════════════════════════════════════════════════

        [Header("Tùy biến XR Rig")]
        public Transform leftHand;
        public Transform rightHand;

        [Header("Tùy biến Gaze Cone")]
        [Range(5f, 30f)]
        public float gazeConeHalfAngle = 20f;
        public float maxRaycastDistance = 20f;
        public LayerMask focusLayerMask = Physics.DefaultRaycastLayers;

        [Header("Tùy biến Proximity (Tay gần mục tiêu)")]
        public float handNearThreshold = 0.30f;

        // ══════════════════════════════════════════════════════════════
        //  PRIVATE STATE
        // ══════════════════════════════════════════════════════════════

        private Camera _mainCamera;
        private Transform _currentQuestTarget;
        private Vector3 _targetVisualCenter;

        private Vector3 _lastHeadPos;
        private float _lastPitchX;
        private float _lastYawY;
        private Vector3 _lastLeftHandPos;
        private Vector3 _lastRightHandPos;

        // ── Bộ đệm RawSample ──
        private struct RawSample
        {
            public float headVelocity;
            public float angularVelX;
            public float angularVelY;
            public float leftHandVel;
            public float rightHandVel;
            public bool isInGazeCone;
            public string focusObjectName;
            public float handDistance;
        }

        private readonly RawSample[] _buffer = new RawSample[150]; // Dự phòng 150 mẫu
        private int _bufferCount = 0;

        // ══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════

        private void Awake()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
                Debug.LogWarning("[SensorHarvester] Không tìm thấy Camera.main!");
        }

        private void Start()
        {
            if (_mainCamera != null)
            {
                _lastHeadPos = _mainCamera.transform.position;
                _lastPitchX = _mainCamera.transform.eulerAngles.x;
                _lastYawY = _mainCamera.transform.eulerAngles.y;
            }

            if (leftHand != null)  _lastLeftHandPos  = leftHand.position;
            if (rightHand != null) _lastRightHandPos = rightHand.position;
        }

        private void OnEnable()
        {
            QuestController.OnTargetTransformChanged += SetCurrentTarget;
        }

        private void OnDisable()
        {
            QuestController.OnTargetTransformChanged -= SetCurrentTarget;
        }

        private void FixedUpdate()
        {
            SampleToBuffer();
        }

        // ══════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════════

        public void SetCurrentTarget(Transform target)
        {
            _currentQuestTarget = target;
            if (target != null)
            {
                var col = target.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    _targetVisualCenter = col.bounds.center;
                }
                else
                {
                    var rend = target.GetComponentInChildren<Renderer>();
                    _targetVisualCenter = rend != null ? rend.bounds.center : target.position;
                }
                Debug.Log($"[SensorHarvester] Mục tiêu mới: {target.name} | Tâm thực: {_targetVisualCenter}");
            }
        }

        /// <summary>
        /// Được gọi mỗi 2 giây từ TelemetryStreamer.
        /// Tổng hợp toàn bộ mẫu trong bộ đệm thành 1 bản ghi duy nhất, reset bộ đệm.
        /// </summary>
        public AggregatedSnapshot AggregateAndFlush(float sessionTimeOffset)
        {
            AggregatedSnapshot snapshot = new AggregatedSnapshot();
            snapshot.time_offset = sessionTimeOffset;
            
            if (_currentQuestTarget != null)
            {
                var currentQuest = _currentQuestTarget.GetComponent<Quest>();
                snapshot.expected_target = currentQuest != null ? currentQuest.Name : _currentQuestTarget.name;
            }
            else
            {
                snapshot.expected_target = "None";
            }

            if (_bufferCount == 0)
            {
                snapshot.focus_object = "None";
                return snapshot;
            }

            float sumHeadVel = 0, sumAngX = 0, sumAngY = 0, sumLeft = 0, sumRight = 0;
            float peakHeadVel = 0, peakAngX = 0, peakAngY = 0, peakLeft = 0, peakRight = 0;

            int focusCount = 0;
            int nearCount = 0;
            float minDist = float.MaxValue;

            Dictionary<string, int> focusFreq = new Dictionary<string, int>();

            for (int i = 0; i < _bufferCount; i++)
            {
                var s = _buffer[i];

                sumHeadVel += s.headVelocity;
                sumAngX += s.angularVelX;
                sumAngY += s.angularVelY;
                sumLeft += s.leftHandVel;
                sumRight += s.rightHandVel;

                if (s.headVelocity > peakHeadVel) peakHeadVel = s.headVelocity;
                if (s.angularVelX > peakAngX) peakAngX = s.angularVelX;
                if (s.angularVelY > peakAngY) peakAngY = s.angularVelY;
                if (s.leftHandVel > peakLeft) peakLeft = s.leftHandVel;
                if (s.rightHandVel > peakRight) peakRight = s.rightHandVel;

                if (s.isInGazeCone) focusCount++;

                if (s.handDistance >= 0f)
                {
                    if (s.handDistance <= handNearThreshold) nearCount++;
                    if (s.handDistance < minDist) minDist = s.handDistance;
                }

                string obj = string.IsNullOrEmpty(s.focusObjectName) ? "None" : s.focusObjectName;
                if (focusFreq.ContainsKey(obj)) focusFreq[obj]++;
                else focusFreq[obj] = 1;
            }

            snapshot.head_vel_avg = sumHeadVel / _bufferCount;
            snapshot.head_vel_peak = peakHeadVel;
            snapshot.ang_vel_x_avg = sumAngX / _bufferCount;
            snapshot.ang_vel_x_peak = peakAngX;
            snapshot.ang_vel_y_avg = sumAngY / _bufferCount;
            snapshot.ang_vel_y_peak = peakAngY;
            snapshot.left_hand_vel_avg = sumLeft / _bufferCount;
            snapshot.left_hand_vel_peak = peakLeft;
            snapshot.right_hand_vel_avg = sumRight / _bufferCount;
            snapshot.right_hand_vel_peak = peakRight;

            snapshot.focus_ratio = (float)focusCount / _bufferCount;
            snapshot.hand_near_ratio = (float)nearCount / _bufferCount;
            snapshot.min_hand_dist = minDist == float.MaxValue ? -1f : minDist;

            string dominantObj = "None";
            int maxFreq = -1;
            foreach (var kvp in focusFreq)
            {
                if (kvp.Value > maxFreq)
                {
                    maxFreq = kvp.Value;
                    dominantObj = kvp.Key;
                }
            }
            snapshot.focus_object = dominantObj;

            // Reset
            _bufferCount = 0;
            return snapshot;
        }

        // ══════════════════════════════════════════════════════════════
        //  PRIVATE LOGIC
        // ══════════════════════════════════════════════════════════════

        private void SampleToBuffer()
        {
            if (_bufferCount >= _buffer.Length) return; // Prevent out-of-bounds

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) dt = 0.02f;

            RawSample sample = new RawSample();
            sample.focusObjectName = "None";
            sample.handDistance = -1f;

            if (_mainCamera != null)
            {
                Vector3 headPos = _mainCamera.transform.position;
                sample.headVelocity = Vector3.Distance(headPos, _lastHeadPos) / dt;
                _lastHeadPos = headPos;

                float pitchX = _mainCamera.transform.eulerAngles.x;
                float yawY = _mainCamera.transform.eulerAngles.y;

                sample.angularVelX = Mathf.Abs(Mathf.DeltaAngle(_lastPitchX, pitchX)) / dt;
                sample.angularVelY = Mathf.Abs(Mathf.DeltaAngle(_lastYawY, yawY)) / dt;

                _lastPitchX = pitchX;
                _lastYawY = yawY;

                // ── Gaze Raycast ──
                string raycastObjName = "None";
                Ray ray = new Ray(headPos, _mainCamera.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, focusLayerMask))
                {
                    var questHit = hit.collider.GetComponentInParent<Quest>();
                    raycastObjName = questHit != null ? questHit.Name : hit.collider.gameObject.name;
                }

                // ── Gaze Cone ──
                if (_currentQuestTarget != null)
                {
                    var col = _currentQuestTarget.GetComponentInChildren<Collider>();
                    if (col != null) _targetVisualCenter = col.bounds.center;

                    Vector3 dirToTarget = _targetVisualCenter - headPos;
                    float angleToTarget = Vector3.Angle(_mainCamera.transform.forward, dirToTarget);

                    var currentQuest = _currentQuestTarget.GetComponent<Quest>();
                    string expectedTargetName = currentQuest != null ? currentQuest.Name : _currentQuestTarget.name;

                    bool inGazeCone = (angleToTarget <= gazeConeHalfAngle);
                    bool raycastHitTarget = (raycastObjName != "None" && raycastObjName == expectedTargetName);

                    if (inGazeCone || raycastHitTarget)
                    {
                        sample.isInGazeCone = true;
                        sample.focusObjectName = expectedTargetName;
                    }
                    else
                    {
                        sample.isInGazeCone = false;
                        sample.focusObjectName = raycastObjName;
                    }
                }
                else
                {
                    sample.isInGazeCone = false;
                    sample.focusObjectName = raycastObjName;
                }
            }

            if (leftHand != null)
            {
                sample.leftHandVel = Vector3.Distance(leftHand.position, _lastLeftHandPos) / dt;
                _lastLeftHandPos = leftHand.position;
            }

            if (rightHand != null)
            {
                sample.rightHandVel = Vector3.Distance(rightHand.position, _lastRightHandPos) / dt;
                _lastRightHandPos = rightHand.position;
            }

            if (_currentQuestTarget != null)
            {
                float leftDist  = leftHand  != null ? Vector3.Distance(leftHand.position,  _targetVisualCenter) : float.MaxValue;
                float rightDist = rightHand != null ? Vector3.Distance(rightHand.position, _targetVisualCenter) : float.MaxValue;
                sample.handDistance = Mathf.Min(leftDist, rightDist);
            }

            _buffer[_bufferCount++] = sample;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_mainCamera == null) _mainCamera = GetComponentInChildren<Camera>();
            if (_mainCamera == null) return;

            Gizmos.color = Color.yellow;
            Vector3 camPos = _mainCamera.transform.position;
            Vector3 forward = _mainCamera.transform.forward;
            Gizmos.DrawRay(camPos, forward * maxRaycastDistance);

            bool isFocusing = false;
            string raycastObjName = "None";
            if (Physics.Raycast(camPos, forward, out RaycastHit hit, maxRaycastDistance, focusLayerMask))
            {
                var questHit = hit.collider.GetComponentInParent<Quest>();
                raycastObjName = questHit != null ? questHit.Name : hit.collider.gameObject.name;
            }

            if (_currentQuestTarget != null)
            {
                var currentQuest = _currentQuestTarget.GetComponent<Quest>();
                string targetName = currentQuest != null ? currentQuest.Name : _currentQuestTarget.name;

                Vector3 dirToTarget = _targetVisualCenter - camPos;
                float angle = Vector3.Angle(forward, dirToTarget);
                
                isFocusing = (angle <= gazeConeHalfAngle) || (raycastObjName != "None" && raycastObjName == targetName);
            }

            Gizmos.color = isFocusing ? Color.green : new Color(1, 0, 0, 0.4f);
            
            int segments = 12;
            float radHalfAngle = gazeConeHalfAngle * Mathf.Deg2Rad;
            float radius = Mathf.Tan(radHalfAngle) * maxRaycastDistance;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2 * Mathf.PI / segments;
                float nextAngle = (i + 1) * 2 * Mathf.PI / segments;

                Vector3 p1Rel = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, maxRaycastDistance);
                Vector3 p2Rel = new Vector3(Mathf.Cos(nextAngle) * radius, Mathf.Sin(nextAngle) * radius, maxRaycastDistance);

                Vector3 p1World = _mainCamera.transform.TransformPoint(p1Rel);
                Vector3 p2World = _mainCamera.transform.TransformPoint(p2Rel);

                Gizmos.DrawLine(camPos, p1World);
                Gizmos.DrawLine(p1World, p2World);
            }

            if (_currentQuestTarget != null)
            {
                Gizmos.color = isFocusing ? Color.green : Color.gray;
                Gizmos.DrawLine(camPos, _targetVisualCenter);
            }
        }
#endif
    }
}
