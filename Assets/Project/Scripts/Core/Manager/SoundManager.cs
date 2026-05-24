using System;
using System.Collections;
using System.Collections.Generic;
using VRAutism.Core;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Inst { get; private set; }

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private List<SoundObject> soundObjects;

    // Lưu delegate để có thể unsubscribe chính xác (lambda tạo instance mới mỗi lần, không thể unsubscribe)
    private Action<object> _onPlaySound;
    private Action<object> _onPauseSound;
    private Action<object> _onPlaySoundLoop;

    private void Awake()
    {
        // Cho phép scene mới ghi đè SoundManager cũ (mỗi scene có danh sách âm thanh riêng)
        Inst = this;

        _onPlaySound = param => PlaySound((TypeSound) param);
        _onPauseSound = param => PauseSound();
        _onPlaySoundLoop = param => PlaySoundLoop((TypeSound) param);

        this.SubscribeListener(EventID.PlaySound, _onPlaySound);
        this.SubscribeListener(EventID.PauseSound, _onPauseSound);
        this.SubscribeListener(EventID.PlaySoundLoop, _onPlaySoundLoop);
    }

    private void OnDestroy()
    {
        // Huỷ đăng ký chính xác bằng biến đã lưu, tránh "xác sống" trong EventChannel
        this.UnsubscribeListener(EventID.PlaySound, _onPlaySound);
        this.UnsubscribeListener(EventID.PauseSound, _onPauseSound);
        this.UnsubscribeListener(EventID.PlaySoundLoop, _onPlaySoundLoop);

        if (Inst == this) Inst = null;
    }

    private bool IsAudioSourceValid()
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[SoundManager] audioSource is null! Trying to find one on this GameObject.");
            audioSource = GetComponent<AudioSource>();
        }
        return audioSource != null;
    }

    private void PlayMusic()
    {
        
    }

    public void PlaySound(TypeSound typeSound)
    {
        if (!IsAudioSourceValid()) return;
        var sound = soundObjects.Find(x => x.typeSound == typeSound);
        if (sound is null) return;
        if (audioSource.isPlaying) audioSource.Stop();
        audioSource.clip = sound.audioClip;
        audioSource.loop = false;
        audioSource.Play();
    }

    public void PlayAudioClip(AudioClip clip)
    {
        if (!IsAudioSourceValid()) return;
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
        if (IsAudioSourceValid())
            audioSource.Pause();
    }

    public void PlaySoundLoop(TypeSound typeSound)
    {
        if (!IsAudioSourceValid()) return;
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
        if (!IsAudioSourceValid()) return;
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
        if (!IsAudioSourceValid()) return false;
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
    Farm1_2,
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


