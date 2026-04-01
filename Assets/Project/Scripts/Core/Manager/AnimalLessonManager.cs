using System.Collections;
using VRAutism.Core;
using UnityEngine;

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
            yield return new WaitForSeconds(timeSoundToDescription);

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

        while (Vector3.Distance(mainCamera.transform.position, targetPosition) > 0.1f)
        {
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPosition, Time.deltaTime * cameraMoveSpeed);
            mainCamera.transform.rotation = Quaternion.Lerp(mainCamera.transform.rotation, targetRotation, Time.deltaTime * cameraMoveSpeed);
            yield return null;
        }
        
        if (end) 
        {
            this.SendEvent(EventID.ExitScene);
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
