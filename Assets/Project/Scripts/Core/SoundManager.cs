using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private List<SoundObject> soundObjects;

    private void Awake()
    {
        this.SubscribeListener(EventID.PlaySound, param => PlaySound((TypeSound) param));
        this.SubscribeListener(EventID.PauseSound, param => PauseSound());
        this.SubscribeListener(EventID.PlaySoundLoop, param => PlaySoundLoop((TypeSound) param));
    }

    private void PlayMusic()
    {
        
    }

    public void PlaySound(TypeSound typeSound)
    {
        var sound = soundObjects.Find(x => x.typeSound == typeSound);
        if (sound is null) return;
        if (audioSource.isPlaying) audioSource.Stop();
        audioSource.clip = sound.audioClip;
        audioSource.loop = false;
        audioSource.Play();
    }

    public void PlayAudioClip(AudioClip clip)
    {
        if (audioSource.isPlaying) audioSource.Stop();
        if (clip != null)
        {
            audioSource.clip = clip;
            audioSource.loop = false;
            audioSource.Play();
        }
    }


    private void PauseSound()
    {
        audioSource.Pause();
    }

    public void PlaySoundLoop(TypeSound typeSound)
    {
        var sound = soundObjects.Find(x => x.typeSound == typeSound);
        if (sound is null) return;
        if (audioSource.isPlaying) audioSource.Stop();
        audioSource.loop = true;
        audioSource.clip = sound.audioClip;
        // audioSource.volume = 0.2f;
        audioSource.Play();
    }

    public void StopLoopingSound()
    {
        if (audioSource.isPlaying && audioSource.loop)
        {
            audioSource.Stop();
            audioSource.loop = false;
        }
    }

    public float GetSoundDuration(TypeSound typeSound)
    {
        var sound = soundObjects.Find(x => x.typeSound == typeSound);
        return sound != null && sound.audioClip != null ? sound.audioClip.length : 0f;
    }

    public bool IsPlaying()
    {
        return audioSource.isPlaying;
    }

}

[Serializable]
public class SoundObject
{
    public TypeSound typeSound;
    public AudioClip audioClip;
}

public enum TypeSound
{
    None,

    // Quizz
    Lose,
    Win,
    GrassLand1,
    GrassLand4,
    GrassLand6,
    GrassLand7,
    GrassLand8,
    GrassLand9,
    GrassLand10,
    Farm1,
    Farm3,
    Farm4,
    Farm5,
    Farm6,
    Farm7,
    Farm8,
    Farm9,
    Farm10,
    Ocean1,
    Ocean5,
    Ocean6,
    Ocean7,
    Ocean8,
    Ocean9,
    Ocean10,

    // Grass Land Animal Lesson
    GrassLandSound,
    GrassLandIntro,
    GrassLandEnd,
    GiraffeSound,
    GiraffeDes,
    RabbitSound,
    RabbitDes,
    ElephantSound,
    ElephantDes,
    ZebraSound,
    ZebraDes,
    LionSound,
    LionDes,

    
    // Farm Animal Lesson
    FarmSound,
    FarmIntro,
    FarmEnd,
    ChickenSound,
    ChickenDesc,
    DogSound,
    DogDes,
    SheepSound,
    SheepDes,
    CowSound,
    CowDes,
    PigSound,
    PigDes,

    // Ocean Animal Lesson
    OceanSound,
    OceanIntro,
    OceanEnd,
    SharkSound,
    SharkDes,
    DolphinSound,
    DolphinDes,
    JellyfishSound,
    JellyfishDes,
    TurtleSound,
    TurtleDes,
    
    // Bathroom
    WaterSound,
    
}


