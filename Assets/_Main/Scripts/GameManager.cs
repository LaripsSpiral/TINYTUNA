using Main.Analytic;
using Main.Character;
using Main.Character.AI;
using Main.Menu;
using Main.Player;
using Main.Score;
using Main.Times;
using Main.WorldStage;
using NaughtyAttributes;
using PrimeTween;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Main
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;

        public bool IsGameStarted { get; private set; }

        [Header("Ref/Camera")]
        [SerializeField]
        private CinemachineCamera mainMenuCamera;

        [SerializeField]
        private CinemachineCamera playerCamera;
        [SerializeField]
        private CinemachineConfiner2D playerConfiner2D;

        [Header("Ref/MainUI")]
        [SerializeField]
        private MainMenuUI mainMenuUI;

        [SerializeField]
        private PauseUI pauseUI;

        [Header("Player")]
        [SerializeField]
        private int maxPlayerHealth = 3;

        [SerializeField]
        private PlayerController player;

        [SerializeField]
        private PlayerHealthUI playerHealthUI;

        [Header("Progress")]
        [SerializeField, ReadOnly]
        private float currentProgress = 0;

        private const float MAX_PROGRESS = 0.35f;

        [SerializeField]
        private ProgressUI progressUI;

        [Header("WorldStage")]
        private WorldStageSystem worldStage => WorldStageSystem.Instance;
        private SpawnerSystem spawner => SpawnerSystem.Instance;

        [Header("FishSpawn")]
        [SerializeField]
        private int startSpawnAmount = 25;

        [Header("Scene Names")]
        [SerializeField]
        private string winSceneName = "WinScene";
        [SerializeField]
        private string loseSceneName = "LoseScene";

        private float startTime;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            IsGameStarted = false;
            TimeScaleSystem.PauseTime();
            mainMenuCamera.gameObject.SetActive(true);
            playerCamera.gameObject.SetActive(false);
            mainMenuUI.ToggleShow(true);

            ScoreSystem.Instance.Reset();
            ScoreUI.Instance.gameObject.SetActive(false);
        }

        private void FixedUpdate()
        {
            if (worldStage.GetCurrentStage() != default && AiFishManager.Instance.FishCount <= 150)
            {
                SpawnFish(player.Character.GetSize() / 1.25f, 20);
            }
        }

        public void StartGame()
        {
            Debug.Log("[GameManager] Setup Start");
            IsGameStarted = true;
            TimeScaleSystem.Resume();

            mainMenuCamera.gameObject.SetActive(false);
            playerCamera.gameObject.SetActive(true);

            progressUI.Init(player.Character.GetSize(), MAX_PROGRESS);
            ScoreUI.Instance.gameObject.SetActive(true);

            // Setup
            player.Character.Setup(health: maxPlayerHealth);
            player.Character.OnTakeDamage += () => playerHealthUI.UpdateHeartUI(player.Character.CurrentHealth);
            player.Character.OnAte += _ => UpdateProgress(player.Character.GetSize());
            player.Character.OnDeath += EndGame;

            AiFishManager.Instance.FetchAllFish();

            if (spawner.enabled)
            {
                worldStage.Init();
                var currentStage = worldStage.GetCurrentStage();

                var playerSize = player.Character.GetSize();
                SpawnFish(currentStage, playerSize / 1.25f, startSpawnAmount/2);
                spawner.SpawnFishInArea(currentStage.GetSpawningFishes(), playerSize / 1.25f, startSpawnAmount/2);
            }

            Debug.Log("[GameManager] Started Game");
            startTime = Time.time;
        }

        private void EndGame()
        {
            Debug.Log("[GameManager] Ended Game");
            LoseGame();
        }

        private void UpdateProgress(float currSize)
        {
            currentProgress = currSize;
            progressUI.UpdateUI(currentProgress);

            switch (currentProgress)
            {
                case < 0.125f:
                    if (worldStage.GetCurrentStage() != worldStage.WorldStageData[0])
                    {
                        NextState();
                    }
                    break;

                case < 0.2f:
                    if (worldStage.GetCurrentStage() != worldStage.WorldStageData[1])
                    {
                        NextState();
                    }
                    break;

                case < 0.25f:
                    if (worldStage.GetCurrentStage() != worldStage.WorldStageData[2])
                    {
                        NextState();
                    }
                    break;

                case < 0.3f:
                    if (worldStage.GetCurrentStage() != worldStage.WorldStageData[3])
                    {
                        NextState();
                    }
                    break;

                case >= MAX_PROGRESS:
                    WinGame();
                    break;
            }
        }

        [Button]
        private void NextState()
        {
            var levelId = worldStage.GetCurrentStage().name;
            var completeDuration = Time.time - startTime;

            // Send Record
            AnalyticManager.Instance.SendRecordDashUsage(levelId);
            AnalyticManager.Instance.SendRecordProgression(levelId);
            AnalyticManager.Instance.SendTimeToCompleteLevel(levelId, completeDuration);

            if (TransitionManager.Instance.CurrentSequence.isAlive)
                return;

            var transition = TransitionManager.Instance.Fade(0, 1);
            transition.ChainCallback(() =>
            {
                var nextWorldStage = worldStage.NextStage();

                var playerSize = player.Character.GetSize();
                SpawnFish(nextWorldStage, scale: playerSize / 1.25f, startSpawnAmount / 2);
                SpawnFish(nextWorldStage, scale: playerSize * 1.25f, 5);

                // Update CameraSize
                playerCamera.Lens.OrthographicSize *= playerSize + 1;
                playerConfiner2D.InvalidateBoundingShapeCache();
            });
            transition.Chain(TransitionManager.Instance.Fade(1, 0));
        }

        private void WinGame()
        {
            Debug.Log("Win");
            TimeScaleSystem.Resume();
            SceneManager.LoadScene(winSceneName);
        }

        private void LoseGame()
        {
            Debug.Log("Lose");
            TimeScaleSystem.Resume();
            SceneManager.LoadScene(loseSceneName);
        }

        public void SpawnFish(float scale, int amount) => SpawnFish(worldStage.GetCurrentStage(), scale, amount);

        private void SpawnFish(WorldStageData worldStageData, float scale, int amount)
        {
            var spawningFishes = worldStageData.GetSpawningFishes();
            spawner.SpawnFish(spawningFishes, scale: scale, amount: amount);
        }
    }
}