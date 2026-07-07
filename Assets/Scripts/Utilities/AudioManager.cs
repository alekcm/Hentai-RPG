using UnityEngine;

namespace RPG.Utilities
{
    /// <summary>
    /// Аудио менеджер
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource voiceSource;

        [Header("Settings")]
        [SerializeField] private float musicVolume = 0.5f;
        [SerializeField] private float sfxVolume = 0.8f;
        [SerializeField] private float voiceVolume = 1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (musicSource == null || clip == null) return;
            musicSource.clip = clip;
            musicSource.loop = loop;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        public void StopMusic()
        {
            if (musicSource != null)
                musicSource.Stop();
        }

        public void PlaySFX(AudioClip clip)
        {
            if (sfxSource == null || clip == null) return;
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        public void PlayVoice(AudioClip clip)
        {
            if (voiceSource == null || clip == null) return;
            voiceSource.clip = clip;
            voiceSource.volume = voiceVolume;
            voiceSource.Play();
        }

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            if (musicSource != null)
                musicSource.volume = musicVolume;
        }

        public void SetSFXVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
        }

        public void SetVoiceVolume(float volume)
        {
            voiceVolume = Mathf.Clamp01(volume);
        }
    }
}
