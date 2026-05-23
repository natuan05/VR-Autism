using System.Collections;
using VRAutism.Core;
using VRAutism.Core.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AnimalLessonManager : MonoBehaviour
{
    [System.Serializable]
    public class Animal
    {
        public AnimalType name;
        public Transform cameraPos;
        public TypeSound sound;
        public TypeSound descriptionSound;
        public float timeDescriptions;
        public GameObject introUI;
    }

    [Header("Lesson Config")]
    [SerializeField] private bool turnOff;
    [SerializeField] private Transform rootPosition;
    
    [Header("VR Settings")]
    [SerializeField] private bool isInVR;
    [SerializeField] private Vector3 offsetVR;
    
    [Header("Camera & Timings")]
    public Transform mainCamera;
    public float cameraMoveSpeed = 2f;
    public float timeSoundToDescription = 4f;
    public float timeIntroSound;
    
    [Header("Audio")]
    public TypeSound backgroundMusic;
    public TypeSound introSound;
    public TypeSound endSound;
    
    [Header("Animals List")]
    public Animal[] animals;

    // ── Runtime helpers — lấy từ SessionContext, fallback về giá trị Inspector ──────
    // Sentinel -1f = không ghi đè; chỉ dùng params khi giá trị >= 0f (hợp lệ từ Firestore).
    private float EffectiveCameraMoveSpeed =>
        SessionContext.Instance != null && SessionContext.Instance.CurrentParams.Exploration.CameraMoveSpeed >= 0f
            ? SessionContext.Instance.CurrentParams.Exploration.CameraMoveSpeed
            : cameraMoveSpeed;

    private float EffectiveSoundToDescriptionGap =>
        SessionContext.Instance != null && SessionContext.Instance.CurrentParams.Exploration.SoundToDescriptionGap >= 0f
            ? SessionContext.Instance.CurrentParams.Exploration.SoundToDescriptionGap
            : timeSoundToDescription;
    
    private void Start()
    {
        foreach (var animal in animals)
        {
            if (animal.introUI != null)
            {
                animal.introUI.SetActive(false);
            }
            else
            {
                Debug.LogWarning($"[AnimalLessonManager] Intro UI for {animal.name} is not found!");
            }
        }

        if (!turnOff)
        {
            StartLesson();
        }
    }

    private void StartLesson()
    {
        TimeManager.Instance.StartLessonTime();
        StartCoroutine(IELesson());
    }

    private IEnumerator IELesson()
    {
        this.SendEvent(EventID.PlaySound, backgroundMusic); 
        yield return new WaitForSeconds(1f);
        
        this.SendEvent(EventID.PlaySound, introSound);
        yield return new WaitForSeconds(timeIntroSound);

        foreach (var animal in animals)
        {
            yield return MoveCameraToAnimal(animal.cameraPos);
            
            this.SendEvent(EventID.PlaySound, animal.descriptionSound);

            if (animal.introUI != null)
                animal.introUI.SetActive(true); 

            yield return new WaitForSeconds(animal.timeDescriptions);
            
            this.SendEvent(EventID.PlaySound, animal.sound);
            yield return new WaitForSeconds(EffectiveSoundToDescriptionGap);

            if (animal.introUI != null)
                animal.introUI.SetActive(false); 
        }
        
        yield return new WaitForSeconds(1f);
        this.SendEvent(EventID.PlaySound, endSound);
        TimeManager.Instance.SaveLessonTimeData();
        
        yield return new WaitForSeconds(5f);
        StartCoroutine(MoveCameraToAnimal(rootPosition, true));
    }

    private IEnumerator MoveCameraToAnimal(Transform animalModel, bool end = false)
    {
        var targetPosition = animalModel.position + (isInVR ? offsetVR : Vector3.zero); 
        var targetRotation = animalModel.rotation;

        // Patch 3+4: Cache tốc độ một lần trước vòng lặp để tránh property lookup dư thừa
        // và đảm bảo giá trị nhất quán trong suốt animation di chuyển.
        // Clamp tối thiểu 0.05f để ngăn infinite loop nếu speed = 0 hoặc âm từ Firestore.
        float cachedSpeed = Mathf.Max(0.05f, EffectiveCameraMoveSpeed);

        while (Vector3.Distance(mainCamera.transform.position, targetPosition) > 0.1f)
        {
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPosition, Time.deltaTime * cachedSpeed);
            mainCamera.transform.rotation = Quaternion.Lerp(mainCamera.transform.rotation, targetRotation, Time.deltaTime * cachedSpeed);
            yield return null;
        }
        
        if (end) 
        {
            SceneManager.LoadScene("GameMenu");
        }
    }
}

public enum AnimalType
{
    // Grass Land
    Rabbit, Zebra, Lion, Elephant, Giraffe,
    // Farm
    Chicken, Sheep, Dog, Pig, Cow,
    // Ocean
    Shark, Jellyfish, Dolphin, Turtle
}
