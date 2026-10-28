using UnityEngine;

namespace RacingPrototype
{
    /// <summary>2D feedback sounds: countdown, checkpoint, lap, finish and warnings.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class RaceAudio : MonoBehaviour
    {
        [SerializeField] AudioClip countdownBeep;
        [SerializeField] AudioClip goBeep;
        [SerializeField] AudioClip checkpointChime;
        [SerializeField] AudioClip lapChime;
        [SerializeField] AudioClip finishFanfare;
        [SerializeField] AudioClip warningBuzz;
        [SerializeField] AudioClip uiClick;

        AudioSource source;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(AudioClip countdown, AudioClip go, AudioClip checkpoint, AudioClip lap, AudioClip finish, AudioClip warning, AudioClip click)
        {
            countdownBeep = countdown; goBeep = go; checkpointChime = checkpoint; lapChime = lap;
            finishFanfare = finish; warningBuzz = warning; uiClick = click;
        }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        void Play(AudioClip clip, float volume = 0.8f)
        {
            if (clip != null && source != null) source.PlayOneShot(clip, volume);
        }

        public void PlayCountdown() => Play(countdownBeep);
        public void PlayGo() => Play(goBeep);
        public void PlayCheckpoint() => Play(checkpointChime, 0.6f);
        public void PlayLap() => Play(lapChime);
        public void PlayFinish() => Play(finishFanfare);
        public void PlayWarning() => Play(warningBuzz, 0.6f);
        public void PlayClick() => Play(uiClick, 0.5f);
    }
}
