using NaughtyAttributes;
using System;
using UnityEngine;
using UnityEngine.XR;

namespace Main.Character
{
    [RequireComponent(typeof(AudioSource))]
    public class Fish : BaseCharacter
    {
        [SerializeField]
        private string fishID;
        public string FishID => fishID;

        [SerializeField]
        protected AudioSource audioSource;

        [SerializeField]
        private AudioClip eatSound;

        public event Action OnDeath;
        public float GetSize() => transform.localScale.y;

        [Header("Eating")]
        [SerializeField]
        protected Transform mouthPos;

        [SerializeField]
        private float mouthSize;
        protected float mouthSizeSqr => mouthSize * GetSize();

        [SerializeField]
        private LayerMask eatingMask;

        [Button]
        private void UpdateID()
        {
            fishID = name;
        }

        private void OnValidate()
        {
            audioSource ??= GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            EatingTarget();
        }

        public void EatingTarget()
        {
            if (mouthPos == default)
                return;

            var hit = Physics2D.CircleCast(mouthPos.position, mouthSizeSqr, transform.forward, mouthSizeSqr, eatingMask);
            if (hit == default || hit.collider.attachedRigidbody == default)
                return;

            if (!hit.collider.attachedRigidbody.TryGetComponent<Fish>(out var fishTarget))
                return;

            if (fishTarget == this)
                return;

            // Return, BiggerFish
            if (fishTarget.GetSize() >= GetSize())
                return;

            Eat(fishTarget);
        }

        protected virtual void Eat(Fish targetFish)
        {
            if (eatSound != null)
                audioSource.PlayOneShot(eatSound);

            targetFish.Eaten();
        }

        protected virtual void Eaten()
        {
            OnDeath?.Invoke();
            SpawnerSystem.Instance.ReturnFishToPool(this);
        }

        private void OnDrawGizmosSelected()
        {
            if (mouthPos == default)
                return;

            Gizmos.DrawWireSphere(mouthPos.position, mouthSizeSqr);
        }
    }
}