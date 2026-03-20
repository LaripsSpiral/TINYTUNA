using Main.Character;
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
        private Dictionary<string, float> analyticEatenFish = new();

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

        public void SendRecordDashUsage(string worldLevel)
        {
            foreach (var kvp in dashTimesByType)
            {
                var record = new CustomEvent("DashUsage");
                record.Add("levelID", worldLevel);
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
                analyticEatenFish.Add(eatenFish.FishID, eatenFish.GetSize());
                return;
            }
        }

        public void SendRecordProgression(string worldLevel)
        {
            foreach (var kvp in analyticEatenFish)
            {
                var record = new CustomEvent("PlayerProgressionInLevels");
                record.Add("levelID", worldLevel);
                record.Add("eatenFishID", kvp.Key);
                record.Add("eatenFishSize", kvp.Value);
                SendRecord(record);
            }

            analyticEatenFish.Clear();
        }

        public void SendTimeToCompleteLevel(string worldLevel, float levelDuration)
        {
            SendRecord(new("CompleteLevelTime")
            {
                {"levelID", worldLevel},
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