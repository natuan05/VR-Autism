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

    [SerializeField] private bool turnOff;
    [SerializeField] private TimeManager timeManager;
    public float timeSoundToDescription=4f;

    public TypeSound backgroundMusic;
    public TypeSound introSound;
    public TypeSound endSound;
    public float timeIntroSound;
    public Transform mainCamera;
    public float cameraMoveSpeed = 2f;
    [SerializeField] private Transform rootPosition;
    [SerializeField] private bool isInVR;
    [SerializeField] private Vector3 offsetVR;
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
                Debug.LogWarning($"Intro UI for {animal.name} is not found!");
            }
        }
        if (turnOff) return;
        StartLesson();
    }

    private void StartLesson()
    {
        timeManager.StartLessonTime();
        StartCoroutine(IELesson());
    }

    private IEnumerator IELesson()
    {
        this.SendEvent(EventID.PlaySound, backgroundMusic); 
        yield return new WaitForSeconds(1f);
        this.SendEvent(EventID.PlaySound, introSound);
        yield return new WaitForSeconds(timeIntroSound);

        for (var i = 0; i < animals.Length; i++)
        {
            var animal = animals[i];
            
            yield return MoveCameraToAnimal(animal.cameraPos);
            this.SendEvent(EventID.PlaySound, animal.descriptionSound);

            if (animal.introUI != null)
            {
                animal.introUI.SetActive(true); 
            }

            yield return new WaitForSeconds(animal.timeDescriptions);
            this.SendEvent(EventID.PlaySound, animal.sound);
            yield return new WaitForSeconds(timeSoundToDescription);

            if (animal.introUI != null)
            {
                animal.introUI.SetActive(false); 
            }
        }
        
        yield return new WaitForSeconds(1f);
        this.SendEvent(EventID.PlaySound, endSound);
        timeManager.SaveLessonTimeData();
        
        yield return new WaitForSeconds(5f);
        
        StartCoroutine(MoveCameraToAnimal(rootPosition, true));
    }

    private IEnumerator MoveCameraToAnimal(Transform animalModel, bool end=false)
    {
        var targetPosition = animalModel.position + (isInVR ? offsetVR : Vector3.zero); 
        var targetRotation = animalModel.rotation;

        while (Vector3.Distance(mainCamera.transform.position, targetPosition) > 0.1f)
        {
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetPosition, Time.deltaTime * cameraMoveSpeed);
            mainCamera.transform.rotation = Quaternion.Lerp(mainCamera.transform.rotation, targetRotation, Time.deltaTime * cameraMoveSpeed);
            yield return null;
        }
        
        if (end) this.SendEvent(EventID.ExitScene);
    }
}

public enum AnimalType
{
    // Grass Land
    Rabbit,
    Zebra,
    Lion,
    Elephant,
    Giraffe,
    // Farm
    Chicken,
    Sheep,
    Dog,
    Pig,
    Cow,
    // Ocean
    Shark,
    Jellyfish,
    Dolphin,
    Turtle
}
