using Main.Analytic;
using Main.Score;
using Main.Times;
using NaughtyAttributes;
using System;
using Unity.VisualScripting;
using UnityEngine;

namespace Main.Character
{
    public class PlayerCharacter : Fish
    {
        public event Action OnDeath;

        [SerializeField]
        private AudioClip takeDamageSound;

        // Update UI
        public event Action OnTakeDamage;
        public event Action<float> OnAte;
        public event Action<float> OnDashCooldownProgress;

        [SerializeField]
        private Collider2D collider2D;

        [SerializeField, ReadOnly]
        private int currentHealth;
        public int CurrentHealth => currentHealth;

        [SerializeField]
        private int iFrameDuration = 1;

        [SerializeField]
        private ComboSystem comboSystem;

        [Header("Buff Settings")]
        [SerializeField, Tooltip("Additional buff multiplier per combo count. Final buff = baseBuff * (1 + comboCount * comboBuffMultiplier)")]
        private float comboBuffMultiplier = 0.1f;

        [Header("KnockBack Settings")]
        [SerializeField]
        private float knockBackRange = 1.0f;
        [SerializeField]
        private float knockBackForce = 1.0f;

        [Header("Dash Settings")]
        [SerializeField]
        private float dashForce = 2f;
        [SerializeField]
        private float dashCooldown = 1f;
        private float lastDashTime = -100f;

        public float DashCooldown => dashCooldown;
        public bool CanDash => Time.time >= lastDashTime + dashCooldown;
        public float DashCooldownProgress
        {
            get
            {
                if (CanDash) return 0f;
                float elapsed = Time.time - lastDashTime;
                return Mathf.Clamp01(1f - (elapsed / dashCooldown));
            }
        }

        private ScoreSystem scoreSystem => ScoreSystem.Instance;

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            Move(moveDir);
        }

        public bool Dash()
        {
            // Check cooldown
            if (!CanDash)
                return false;

            RecordDash();

            Vector2 dir = moveDir.normalized;
            if (dir == Vector2.zero)
            {
                if (Rb2d.linearVelocity.sqrMagnitude > 0.1f)
                    dir = Rb2d.linearVelocity.normalized;
                else
                    dir = transform.right;
            }

            Rb2d.AddForce(dir * dashForce, ForceMode2D.Impulse);
            lastDashTime = Time.time;

            // Start cooldown timer with progress updates
            if (CountDownTimer.Instance != null)
            {
                string id = $"DashCooldown";
                var cdEvent = new CountDownEvent(
                    OnFinished: null,
                    CoolDownTime: dashCooldown
                );
                // Forward per-frame progress to listeners (remaining ratio goes 1 -> 0)
                cdEvent.OnTick = (ratio) => OnDashCooldownProgress?.Invoke(ratio);
                CountDownTimer.Instance.AddCountDownEvent(id, cdEvent);
            }

            return true;
        }

        private void RecordDash()
        {
            // Determine dash direction
            Vector2 dir = moveDir.normalized;
            if (dir == Vector2.zero)
            {
                if (Rb2d != null && Rb2d.linearVelocity.sqrMagnitude > 0.1f)
                    dir = Rb2d.linearVelocity.normalized;
                else
                    dir = transform.right;
            }

            // Detection parameters
            const float detectionRadius = 2.0f; // radius to look for nearby fishes (tunable)
            const float forwardConeCos = 0.7f;  // cos(45deg) ~ 0.707; a fish with dot >= this is roughly in front

            DashType type = DashType.Movement;

            // Find nearby colliders
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRadius);
            if (hits != null && hits.Length > 0)
            {
                bool foundAteCandidate = false;
                bool foundThreat = false;

                foreach (var col in hits)
                {
                    if (col == null || col.attachedRigidbody == null)
                        continue;

                    var rb = col.attachedRigidbody;

                    // Skip self
                    if (rb.gameObject == gameObject)
                        continue;

                    if (rb.TryGetComponent(out Fish otherFish))
                    {
                        float otherSize = otherFish.GetSize();
                        float mySize = GetSize();

                        Vector2 toOther = (rb.position - (Vector2)transform.position);
                        float dist = toOther.magnitude;
                        if (dist <= Mathf.Epsilon)
                            continue;

                        Vector2 toOtherDir = toOther.normalized;
                        float forwardDot = Vector2.Dot(dir, toOtherDir);

                        // If there's a smaller fish roughly in front within the detection radius,
                        // assume dash was intended to eat (Ate).
                        if (otherSize < mySize && forwardDot >= forwardConeCos)
                        {
                            foundAteCandidate = true;
                            break; // Prefer Ate, no need to search further
                        }

                        // If there's a larger fish nearby, consider this a possible flee dash.
                        if (otherSize > mySize && dist <= detectionRadius)
                        {
                            // Optionally check if the larger fish is close enough or roughly in pursuit direction.
                            foundThreat = true;
                        }
                    }
                }

                if (foundAteCandidate)
                    type = DashType.Ate;
                else if (foundThreat)
                    type = DashType.Flee;
                else
                    type = DashType.Movement;
            }

            AnalyticManager.Instance.AddDashRecord(type);
        }

        private void Start()
        {
            // Auto-find combo system if not assigned in inspector
            if (comboSystem == null)
            {
                comboSystem = FindAnyObjectByType<ComboSystem>();
            }
        }

        public void Setup(int health)
        {
            currentHealth = health;
        }

        public void UpdateMoveInput(Vector2 moveInput)
        {
            moveDir = moveInput;
        }

        protected override void RotateAlongVelocity()
        {
            Vector2 dir = moveDir;

            if (dir.sqrMagnitude < 0.001f)
                return;

            float rotAngleZ = FastAtan2(dir.y, dir.x) * Mathf.Rad2Deg;

            float finalRotZ;
            float rotAngleY;

            if (dir.x < 0)
            {
                rotAngleY = 180f;
                finalRotZ = 180f - rotAngleZ;
            }
            else
            {
                rotAngleY = 0f;
                finalRotZ = rotAngleZ;
            }

            var rotZ = Mathf.LerpAngle(transform.localEulerAngles.z, finalRotZ, 0.2f);

            var newRot = new Vector3(0f, rotAngleY, rotZ);

            transform.localEulerAngles = newRot;
        }

        protected override void Eat(Fish targetFish)
        {
            AnalyticManager.Instance.AddAteFishRecord(targetFish);

            int comboCount = comboSystem != null ? comboSystem.ComboCount : 0;

            base.Eat(targetFish);
            transform.localScale += Vector3.one * (targetFish.GetSize() / 200) + (Vector3.one * comboCount / 100000);
            Debug.Log($"[Player Size] update : {GetSize()}");
            OnAte?.Invoke(targetFish.GetSize());

            CreateKnockbackWave(radius: knockBackRange, baseForce: knockBackForce);

            comboSystem?.AddCombo();
            scoreSystem?.AddScore(targetFish.GetSize() * 100 + (30 * comboCount));

            // Scale buff based on current combo count
            float baseBuff = targetFish.GetSize();
            float finalBuff = baseBuff * (1f + comboCount * comboBuffMultiplier);

            var newBuffTotal = AddTimedSpeedBuff(finalBuff, 5f);
            Debug.Log($"{this} applied speed buff (base:{baseBuff} combo:{comboCount} final:{finalBuff:F2}), current total SpeedBuff={newBuffTotal}");
        }
        protected override void Eaten()
        {
            TakeDamage();
        }

        private void TakeDamage()
        {
            // IFrame
            if (collider2D.enabled == false)
                return;

            // Do IFrame
            collider2D.enabled = false;

            var countDownEvent = new CountDownEvent(
                OnFinished: () => collider2D.enabled = true,
                CoolDownTime: iFrameDuration
            );

            CountDownTimer.Instance.AddCountDownEvent("PlayerIFrame", countDownEvent);

            currentHealth -= 1;
            Debug.Log($"{this} Taken Damage, left {currentHealth} Health");

            CreateKnockbackWave(radius: knockBackRange, baseForce: knockBackForce);

            audioSource.PlayOneShot(takeDamageSound);
            OnTakeDamage?.Invoke();

            // Check Death
            if (currentHealth > 0)
                return;

            Debug.Log($"{this} have no Health left");
            Death();
        }

        protected override void Death()
        {
            gameObject.SetActive(false);
            OnDeath.Invoke();
        }

        private void CreateKnockbackWave(float radius, float baseForce)
        {
            // Collect all colliders in radius
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);
            if (hits == null || hits.Length == 0)
                return;

            Vector2 center = transform.position;
            const float epsilon = 0.0001f;

            foreach (var col in hits)
            {
                if (col.attachedRigidbody == null)
                    continue;

                var rb = col.attachedRigidbody;

                // Don't apply to self
                if (rb.gameObject == gameObject)
                    continue;

                if (rb.TryGetComponent(out Fish otherFish))
                {
                    if (otherFish.GetSize() > GetSize())
                        return;
                }

                Vector2 otherPos = rb.position;
                Vector2 dir = otherPos - center;
                float dist = dir.magnitude;

                if (dist < epsilon)
                {
                    // If overlapping exactly, push in a random direction to avoid NaN
                    dir = UnityEngine.Random.insideUnitCircle.normalized;
                    dist = epsilon;
                }

                // Attenuate force by distance (closer => stronger)
                float attenuation = Mathf.Clamp01(1f - (dist / radius));
                float forceMagnitude = baseForce * attenuation;

                Vector2 force = dir.normalized * forceMagnitude;

                // Add a slight upward bias (relative to world up) for visual variety
                force += Vector2.up * (attenuation * baseForce);
                rb.AddForce(force, ForceMode2D.Impulse);
            }
        }
    }
}