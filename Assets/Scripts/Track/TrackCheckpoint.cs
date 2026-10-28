using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// A trigger gate across the track. It reports a crossing only when a car enters from one side
    /// and leaves from the other, so driving in and backing out never counts, and the direction
    /// (with or against the track) is known. Index 0 is the start/finish line. The crossing time is
    /// interpolated to the moment the car's centre passed the gate plane, so timing does not depend
    /// on the physics step.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TrackCheckpoint : MonoBehaviour
    {
        [SerializeField] int index;
        [SerializeField] LapManager lapManager;
        [Tooltip("Gate parts tinted to show which checkpoint the player needs next.")]
        [SerializeField] Renderer[] highlightRenderers;
        [SerializeField] Color idleColour = new Color(0.12f, 0.13f, 0.16f);
        [SerializeField] Color nextColour = new Color(0.09f, 0.64f, 0.29f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        readonly Dictionary<RaceParticipant, float> entrySide = new Dictionary<RaceParticipant, float>();
        MaterialPropertyBlock block;

        public int Index => index;
        public bool IsFinishLine => index == 0;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(int checkpointIndex, LapManager manager, Renderer[] highlights)
        {
            index = checkpointIndex;
            lapManager = manager;
            highlightRenderers = highlights;
        }

        void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            SetHighlighted(false);
        }

        public void ClearTracking() => entrySide.Clear();

        public void SetHighlighted(bool isNext)
        {
            if (highlightRenderers == null) return;
            block ??= new MaterialPropertyBlock();
            Color c = isNext ? nextColour : idleColour;
            foreach (var r in highlightRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(EmissionId, isNext ? c * 1.6f : Color.black);
                r.SetPropertyBlock(block);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var participant = ParticipantOf(other);
            if (participant == null) return;
            entrySide[participant] = SideOf(participant.transform.position);
        }

        void OnTriggerExit(Collider other)
        {
            var participant = ParticipantOf(other);
            if (participant == null || !entrySide.TryGetValue(participant, out float enteredAt)) return;
            entrySide.Remove(participant);

            float exitedAt = SideOf(participant.transform.position);
            if (lapManager == null) return;
            bool forward = enteredAt < 0f && exitedAt > 0f;
            bool backward = enteredAt > 0f && exitedAt < 0f;
            if (!forward && !backward) return;

            // Back-date to the instant the car centre crossed the gate plane.
            float speed = Mathf.Abs(Vector3.Dot(participant.Car.Velocity, transform.forward));
            float crossingTime = Time.time - Mathf.Abs(exitedAt) / Mathf.Max(speed, 1f);
            lapManager.RegisterCrossing(participant, index, forward, crossingTime);
        }

        /// <summary>Negative = before the gate, positive = past it (in the driving direction).</summary>
        float SideOf(Vector3 worldPosition) => Vector3.Dot(worldPosition - transform.position, transform.forward);

        static RaceParticipant ParticipantOf(Collider other)
        {
            var body = other.attachedRigidbody;
            return body != null ? body.GetComponent<RaceParticipant>() : null;
        }
    }
}
