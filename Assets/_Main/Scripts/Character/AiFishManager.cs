using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Main.Character.AI
{
    public class AiFishManager : MonoBehaviour
    {
        public static AiFishManager Instance;

        [SerializeField]
        private RectTransform aiFishArea;

        [SerializeField]
        private PlayerCharacter playerCharacter;

        [SerializeField, ReadOnly]
        private List<Fish> fishList = new List<Fish>();
        public int FishCount => fishList.Count;

        private JobHandle jobHandle;

        public const float VISION_RANGE = 5f;
        public const float VISION_ANGLE = 120f;

        public const float FOCUS_TARGET_DURATION = 2f;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (fishList.Count == 0)
                return;

            var fishesCount = fishList.Count;
            var inputData = new NativeArray<FishJobInput>(fishesCount, Allocator.TempJob);
            var outputResults = new NativeArray<FishJobOutput>(fishesCount, Allocator.TempJob);

            // Get data
            for (int i = 0; i < fishesCount; i++)
            {
                // Fish Data
                var fish = fishList[i];
                var transform = fish.transform;
                var state = (int)State.Idle;
                var focusingTime = 0f;
                var targetPos = new float2(transform.position.x, transform.position.y);
                var isPlayer = fish is PlayerCharacter;

                // Get AI State if possible
                if (fish.TryGetComponent<AiFish>(out var aiFish))
                {
                    state = (int)aiFish.CurrentState;
                    focusingTime = aiFish.FocusingTime;
                    targetPos = new float2(aiFish.TargetPosition.x, aiFish.TargetPosition.y);
                }

                // Fill Input Data
                inputData[i] = new FishJobInput
                {
                    Index = i,
                    Position = new float2(transform.position.x, transform.position.y),
                    ForwardDirection = new float2(transform.right.x, transform.right.y),
                    CurrentTargetPosition = targetPos,
                    Size = fish.GetSize(),
                    CurrentStateInt = state,
                    FocusingTime = focusingTime,
                    IsPlayer = isPlayer,
                };
            }

            // Job Scheduling
            var aiJob = new FishAIJob
            {
                FishesInput = inputData,
                OutputResults = outputResults,
                MaxSearchDistance = VISION_RANGE * VISION_RANGE,
                MaxVisionAngle = math.cos(math.radians(VISION_ANGLE) / 2f),
                CurrentTime = Time.time,
                AreaCenter = new float2(aiFishArea.position.x, aiFishArea.position.y),
                AreaSize = new float2(aiFishArea.rect.width, aiFishArea.rect.height)
            };

            jobHandle = aiJob.Schedule(fishList.Count, jobHandle);

            // Wait for job
            jobHandle.Complete();

            // Apply
            for (int i = 0; i < fishList.Count; i++)
            {
                var fish = fishList[i];

                if (!fish.TryGetComponent<AiFish>(out var aiFish))
                    continue;

                var output = outputResults[i];
                aiFish.TargetPosition = output.TargetPosition;
                aiFish.CurrentState = (State)output.StateInt;
                aiFish.FocusingTime = output.FocusingTime;

                aiFish.UpdateMovement();
            }

            inputData.Dispose();
            outputResults.Dispose();
        }

        public void FetchAllFish()
        {
            fishList.Clear();

            // Prefer the serialized field, fallback to scene lookup for compatibility and inspector convenience
            var player = playerCharacter != null ? playerCharacter : FindFirstObjectByType<PlayerCharacter>();
            if (player != null)
            {
                if (!fishList.Contains(player))
                {
                    fishList.Add(player);
                    player.OnDeath += () => fishList.Remove(player);
                }
            }
            else
            {
                Debug.LogWarning("[AiFishManager] PlayerCharacter not assigned in inspector and not found in scene.");
            }

            // Use FindObjectsOfType for broader Unity version compatibility
            var aiFishes = FindObjectsByType<AiFish>(sortMode: FindObjectsSortMode.InstanceID);
            foreach (var aiFish in aiFishes)
            {
                if (aiFish == null)
                    continue;

                if (!fishList.Contains(aiFish))
                {
                    fishList.Add(aiFish);
                    aiFish.OnDeath += () => fishList.Remove(aiFish);
                }
            }

            Debug.Log($"[AiFishManager] Fetched {fishList.Count} Fishes. ({aiFishes.Length} AI)");
        }

        private void OnDrawGizmos()
        {
            if (aiFishArea == null)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(aiFishArea.position, new Vector3(aiFishArea.rect.width, aiFishArea.rect.height, 1f));
        }
    }

    public enum State
    {
        Idle = 0,
        Hunting = 1,
        Fleeing = 2,
    }

    public struct FishJobInput
    {
        public int Index;
        public float2 Position;
        public float2 ForwardDirection;
        public float2 CurrentTargetPosition;
        public float Size;
        public int CurrentStateInt;
        public float FocusingTime;
        public bool IsPlayer;
    }

    public struct FishJobOutput
    {
        public float2 TargetPosition;
        public int StateInt; // For Job
        public float FocusingTime;
    }

    [BurstCompile]
    public struct FishAIJob : IJobFor
    {
        // Input
        [ReadOnly]
        public NativeArray<FishJobInput> FishesInput;

        [ReadOnly]
        public float MaxSearchDistance;

        [ReadOnly]
        public float MaxVisionAngle;

        [ReadOnly]
        public float CurrentTime;

        [ReadOnly]
        public float2 AreaCenter;

        [ReadOnly]
        public float2 AreaSize;

        // Output
        [WriteOnly]
        public NativeArray<FishJobOutput> OutputResults;

        public void Execute(int index)
        {
            // Self
            var ownInput = FishesInput[index];
            var ownSize = ownInput.Size;
            var ownPosition = ownInput.Position;
            var closestTarget = MaxSearchDistance;

            // Target
            var newTargetPos = ownInput.CurrentTargetPosition;
            var newState = ownInput.CurrentStateInt;

            var isFoundTarget = false;
            var focusingTime = ownInput.FocusingTime;

            // Priority targets: player first for all hunters, but aggressive prioritizes more
            float2 playerTargetPos = float2.zero;
            float playerDistance = MaxSearchDistance;
            bool foundPlayer = false;
            float2 closestPreyPos = float2.zero;
            float closestPreyDistance = MaxSearchDistance;

            // Find target - prioritize player first
            for (int i = 0; i < FishesInput.Length; i++)
            {
                // Skip self
                if (i == index)
                    continue;

                var otherFish = FishesInput[i];
                var distance = math.lengthsq(otherFish.Position - ownPosition);

                // Out of Vision Range
                if (distance > MaxSearchDistance)
                    continue;

                // Out of Vision Cone
                if (!IsInVisionCone(ownPosition, ownInput.ForwardDirection, otherFish.Position, MaxVisionAngle))
                    continue;

                // Check if target is player - prioritize player first for all hunters
                if (otherFish.IsPlayer && otherFish.Size < ownSize)
                {
                    playerTargetPos = otherFish.Position;
                    playerDistance = distance;
                    foundPlayer = true;
                }

                // Found Prey (non-player)
                if (!otherFish.IsPlayer && otherFish.Size < ownSize)
                {
                    if (distance < closestPreyDistance)
                    {
                        closestPreyPos = otherFish.Position;
                        closestPreyDistance = distance;
                    }
                }

                // Found Predator
                else if (otherFish.Size > ownSize)
                {
                    // Fleeing
                    if (distance < closestTarget * 1.5f || newState != (int)State.Fleeing)
                    {
                        var fleeDir = math.normalize(ownPosition - otherFish.Position);
                        newTargetPos = ownPosition + fleeDir * 20;
                        newState = (int)State.Fleeing;
                        closestTarget = distance;
                        isFoundTarget = true;

                        focusingTime = CurrentTime + AiFishManager.FOCUS_TARGET_DURATION;
                    }
                }
            }

            // Apply target: prioritize player first for all hunters
            if (foundPlayer)
            {
                newTargetPos = playerTargetPos;
                newState = (int)State.Hunting;
                isFoundTarget = true;
                focusingTime = CurrentTime + AiFishManager.FOCUS_TARGET_DURATION;
            }
            else if (closestPreyDistance < MaxSearchDistance)
            {
                // Hunt closest prey if no player found
                newTargetPos = closestPreyPos;
                newState = (int)State.Hunting;
                isFoundTarget = true;
                focusingTime = CurrentTime + AiFishManager.FOCUS_TARGET_DURATION;
            }

            // Check if current hunt target is out of bounds - return to center as priority
            if (newState == (int)State.Hunting && isFoundTarget)
            {
                bool isHuntTargetOutOfBounds =
                    math.abs(newTargetPos.x - AreaCenter.x) > AreaSize.x / 2f ||
                    math.abs(newTargetPos.y - AreaCenter.y) > AreaSize.y / 2f;

                if (isHuntTargetOutOfBounds)
                {
                    // Return to center as priority
                    newTargetPos = AreaCenter;
                    newState = (int)State.Idle;
                    isFoundTarget = true;
                    focusingTime = 0f;
                }
            }

            // No target
            if (!isFoundTarget)
            {
                // Check bounds
                bool isOutOfBounds =
                    math.abs(ownPosition.x - AreaCenter.x) > AreaSize.x / 2f ||
                    math.abs(ownPosition.y - AreaCenter.y) > AreaSize.y / 2f;

                if (isOutOfBounds && newState != (int)State.Hunting && newState != (int)State.Fleeing)
                {
                    // Return to center
                    newTargetPos = AreaCenter;
                    newState = (int)State.Idle;
                    isFoundTarget = true;
                    focusingTime = 0f;
                }

                switch (newState)
                {
                    case (int)State.Fleeing:
                        if (CurrentTime >= focusingTime)
                        {
                            newState = (int)State.Idle;
                            focusingTime = 0f;
                        }
                        else
                        {
                            // Keep moving to target
                            isFoundTarget = true;
                            newTargetPos = ownPosition + ownInput.ForwardDirection * 20f;
                        }
                        break;

                    case (int)State.Hunting:
                        if (CurrentTime >= focusingTime)
                        {
                            newState = (int)State.Idle;
                            focusingTime = 0f;
                        }
                        else
                        {
                            // Keep moving to target
                            isFoundTarget = true;
                            newTargetPos = ownPosition + ownInput.ForwardDirection * 20f;
                        }
                        break;

                    case (int)State.Idle:

                        // Check if target is valid (in bounds) and if we reached it
                        bool isTargetOutOfBounds =
                            math.abs(newTargetPos.x - AreaCenter.x) > AreaSize.x / 2f ||
                            math.abs(newTargetPos.y - AreaCenter.y) > AreaSize.y / 2f;

                        float dist = math.distance(ownPosition, newTargetPos);

                        if (dist < 2f || isTargetOutOfBounds)
                        {
                            // Random Wandering Position
                            var randomSeed = (uint)((ownInput.Index * 1000f) + (ownPosition.x * 100) + (ownPosition.y * 100) + (CurrentTime * 1000));
                            Unity.Mathematics.Random random = new Unity.Mathematics.Random(randomSeed);

                            var halfSize = AreaSize / 2f;
                            newTargetPos.x = random.NextFloat(AreaCenter.x - halfSize.x, AreaCenter.x + halfSize.x);
                            newTargetPos.y = random.NextFloat(AreaCenter.y - halfSize.y, AreaCenter.y + halfSize.y);
                        }
                        newState = (int)State.Idle;
                        break;
                }
            }

            // Write output
            OutputResults[index] = new FishJobOutput
            {
                TargetPosition = newTargetPos,
                StateInt = newState,
                FocusingTime = focusingTime,
            };
        }

        private static bool IsInVisionCone(float2 ownPos, float2 ownForward, float2 targetPos, float cosAngleThreshold)
        {
            float2 directionToTarget = math.normalize(targetPos - ownPos);
            float dotProduct = math.dot(ownForward, directionToTarget);

            // If the dot product is greater than the cosine of the half-angle, the target is in the cone.
            return dotProduct >= cosAngleThreshold;
        }
    }
}