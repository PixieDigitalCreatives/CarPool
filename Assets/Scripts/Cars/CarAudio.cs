using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Looping engine tone whose pitch climbs through simulated gears with speed.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class CarAudio : MonoBehaviour
    {
        [SerializeField] AudioClip engineLoop;
        [SerializeField] int gears = 5;
        [SerializeField] float playerVolume = 0.35f;
        [SerializeField] float aiVolume = 0.5f;

        AudioSource source;
        CarController car;
        RaceParticipant participant;

        public void Configure(AudioClip clip) => engineLoop = clip;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            car = GetComponent<CarController>();
            participant = GetComponent<RaceParticipant>();
        }

        void Start()
        {
            if (engineLoop == null) { enabled = false; return; }
            bool isPlayer = participant != null && participant.IsPlayer;
            source.clip = engineLoop;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = isPlayer ? 0f : 1f;
            source.minDistance = 6f;
            source.maxDistance = 70f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0.3f;
            source.volume = isPlayer ? playerVolume : aiVolume;
            source.Play();
        }

        void Update()
        {
            if (car == null) return;
            float s = car.NormalisedSpeed;
            float gearProgress = s * gears;
            float inGear = gearProgress - Mathf.Floor(Mathf.Min(gearProgress, gears - 0.001f));
            float pitch = 0.55f + s * 0.6f + inGear * 0.5f;
            if (!car.ControlsEnabled) pitch = 0.55f;
            source.pitch = Mathf.Lerp(source.pitch, pitch, 10f * Time.deltaTime);
        }
    }
}
