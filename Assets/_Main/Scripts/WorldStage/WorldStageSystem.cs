using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Main.WorldStage
{
    public class WorldStageSystem : MonoBehaviour
    {
        public static WorldStageSystem Instance;

        [SerializeField, ReadOnly]
        private Queue<WorldStageData> worldStageQueue = new();

        public WorldStageData CurrentWorldStage => worldStageQueue.Peek();

        [SerializeField]
        private WorldStageData[] worldStageData;
        public WorldStageData[] WorldStageData => worldStageData;

        [SerializeField]
        private Transform container;

        [SerializeField]
        private GameObject currentDecoration;

        [SerializeField]
        private GameObject currentSky;

        public void Awake()
        {
            Instance = this;
        }

        public void Init() 
        {
            worldStageQueue.Clear();

            foreach (var data in worldStageData)
            {
                worldStageQueue.Enqueue(data);
            }

            UpdateWorldScene(worldStageQueue.Peek());
        }
        public WorldStageData GetCurrentStage()
        {
            if (!CheckRemainingStage())
                return default;

            return worldStageQueue.Peek();
        }

        public WorldStageData NextStage()
        {
            if (!CheckRemainingStage())
                return default;

            worldStageQueue.Dequeue();
            var nextStage = worldStageQueue.Peek();
            Debug.Log($"[WorldStageSystem] Updated Stage {nextStage}");
            UpdateWorldScene(nextStage);
            return nextStage;
        }

        private bool CheckRemainingStage()
        {
            if (worldStageQueue.Count == 0)
            {
                Debug.Log($"[WorldStageSystem] Out of stage");
                return false;
            }

            return true;
        }

        private void UpdateWorldScene(WorldStageData worldStageData)
        {
            if (worldStageData == default)
                return;

            if (currentDecoration != null)
            {
                Destroy(currentDecoration);
                currentDecoration = Instantiate(worldStageData.GetDecoration(), container);
            }

            if (currentSky != null)
            {
                Destroy(currentSky);
                currentSky = Instantiate(worldStageData.GetSky(), container);
            }
        }
    }
}