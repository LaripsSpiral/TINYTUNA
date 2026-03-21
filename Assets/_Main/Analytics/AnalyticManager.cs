using Main.Character;
using System;
using System.Collections.Generic;
using Unity.Services.Analytics;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;
using UnityEngine.UnityConsent;

namespace Main.Analytic
{
    public class AnalyticManager : MonoBehaviour
    {
        private Dictionary<string, int> dashTimesByType = new();
        private Dictionary<string, int> analyticEatenFish = new();

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
            if (!analyticEatenFish.ContainsKey(eatenFish.FishID))
            {
                analyticEatenFish.Add(eatenFish.FishID, 1);
                return;
            }

            analyticEatenFish[eatenFish.FishID]++;
        }

        public void SendRecordProgression(string worldStageID)
        {
            foreach (var kvp in analyticEatenFish)
            {
                var record = new CustomEvent("PlayerProgressionInLevels");
                record.Add("levelID", worldStageID);
                record.Add("eatenFishID", kvp.Key);
                record.Add("eatenFishAmount", kvp.Value);
                SendRecord(record);
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