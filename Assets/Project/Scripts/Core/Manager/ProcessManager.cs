using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VRAutism.Core
{
    public class ProcessManager : MonoBehaviour
    {
        [SerializeField] private Transform processUI;
        [SerializeField] private GameObject congratulationsUI;
        [SerializeField] private int maxProcess;

        private Slider _processSlider;
        private int _currentProcessID;
        private bool _trigger;
        private float _time;
        private float _maxTime;

        private readonly Vector3 _offset = new(0, 0.1f, 0);

        private void Awake()
        {
            _processSlider = processUI.GetComponentInChildren<Slider>();
            _currentProcessID = 0;
            processUI.gameObject.SetActive(false);
            congratulationsUI.SetActive(false);

            this.SubscribeListener(EventID.OnTriggerProcessEnter, param => StartCurrentProcess((Process)param));
            this.SubscribeListener(EventID.OnTriggerProcessExit, param => StopCurrentProcess((Process)param));
        }

        private void StartCurrentProcess(Process process)
        {
            if (_currentProcessID != process.ID) return;
            _trigger = true;
            _maxTime = process.Time;
            _time = 0;
            processUI.gameObject.SetActive(true);
            processUI.position = process.Position + _offset;
        }

        private void StopCurrentProcess(Process process)
        {
            if (_currentProcessID == process.ID) return;
            _trigger = false;
            processUI.gameObject.SetActive(false);
        }

        private void CompleteCurrentProcess()
        {
            _currentProcessID++;
            processUI.gameObject.SetActive(false);

            if (_currentProcessID == maxProcess)
            {
                congratulationsUI.transform.localScale = Vector3.zero;
                congratulationsUI.SetActive(true);
                congratulationsUI.transform.DOScale(0, 0.5f).SetEase(Ease.OutBounce);
            }
        }

        private void Update()
        {
            if (_trigger)
            {
                _time += Time.deltaTime;
                _processSlider.value = _time / _maxTime;

                if (_time >= _maxTime)
                {
                    _trigger = true;
                    CompleteCurrentProcess();
                }
            }
        }
    }

    public class Process
    {
        public Vector3 Position;
        public int ID;
        public int Time;
    }
}
