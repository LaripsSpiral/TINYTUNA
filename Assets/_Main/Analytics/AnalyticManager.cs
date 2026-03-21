using Main.Character;
using Main.WorldStage;
using System;
using System.Collections.Generic;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UnityConsent;

namespace Main.Analytic
{
    public class AnalyticManager : MonoBehaviour
    {
        private Dictionary<string, int> dashTimesByType = new();
        private List<CustomEvent> analyticEatenFish = new();

        public static AnalyticManager Instance { get; set; }

        private void Awake()
        {
            Instance ??= this;
            DontDestroyOnLoad(this);
        }

        private void Start()
        {
            UnityServices.InitializeAsync();
            EndUserConsent.SetConsentState(new ConsentState
            {
                AnalyticsIntent = ConsentStatus.Granted,
                AdsIntent = ConsentStatus.Denied
            });
        }

        private void SendRecord(CustomEvent recordEvent)
        {
            AnalyticsService.Instance.RecordEvent(recordEvent);
        }


        public void AddDashRecord(DashType dashType)
        {
            if (!dashTimesByType.ContainsKey(dashType.ToString()))
            {
                dashTimesByType.Add(dashType.ToString(), 1);
                return;
            }

            dashTimesByType[dashType.ToString()]++;
        }

        public void SendRecordDashUsage(string worldStageID)
        {
            foreach (var kvp in dashTimesByType)
            {
                var record = new CustomEvent("DashUsage");
                record.Add("levelID", worldStageID);
                record.Add("detail", kvp.Key);
                record.Add("UsageTimes", kvp.Value);
                SendRecord(record);
            }

            dashTimesByType.Clear();
        }

        public void AddAteFishRecord(Fish eatenFish)
        {
            analyticEatenFish.Add(new("PlayerProgressionInLevels")
            {
                {"eatenFishID", eatenFish.FishID },
                { "eatenFishSize", eatenFish.GetSize() },
            });
        }

        public void SendRecordProgression(string worldStageID)
        {
            foreach (var customEvent in analyticEatenFish)
            {
                customEvent.Add("levelID", worldStageID);
                SendRecord(customEvent);
            }

            analyticEatenFish.Clear();
        }

        public void SendTimeToCompleteLevel(string worldStageID, float levelDuration)
        {
            SendRecord(new("CompleteLevelTime")
            {
                {"levelID", worldStageID},
                {"timer", levelDuration}
            });
        }
    }

    public enum DashType
    {
        Movement,
        Ate,
        Flee,
    }
}