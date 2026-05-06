using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public class FacilitiesService
{
    public const string DefaultPlayerId = "local_player";
    private const string PlayerFacilitiesFileName = "player_facilities.json";

    // facility_type_id -> Resources file name
    private readonly Dictionary<string, string> _facilityResourceMap = new()
    {
        { "weight_room", "WeightRoom" },
        { "rehab_center", "Rehab" },
        { "film_room", "Film" }
    };

    /// <summary>
    /// Upgrades a facility by one level for the given player.
    /// Returns the updated PlayerFacilityProgress in newState.
    /// Economy/EventBus integration can be added later before SavePlayerFacilityState().
    /// </summary>
    public bool TryUpgradeFacility(string playerId, string facilityTypeId, out PlayerFacilityProgress newState)
    {
        newState = null;

        if (string.IsNullOrWhiteSpace(playerId))
        {
            Debug.LogError("FacilitiesService.TryUpgradeFacility: playerId is required.");
            return false;
        }

        if (!IsValidFacilityType(facilityTypeId))
        {
            Debug.LogError($"FacilitiesService.TryUpgradeFacility: invalid facilityTypeId '{facilityTypeId}'.");
            return false;
        }

        var config = LoadFacilityConfig(facilityTypeId);
        if (config == null || config.levels == null || config.levels.Count == 0)
        {
            Debug.LogError($"FacilitiesService.TryUpgradeFacility: failed to load config for '{facilityTypeId}'.");
            return false;
        }

        var playerState = GetPlayerFacilityState(playerId);
        var progress = GetOrCreateFacilityProgress(playerState, facilityTypeId);

        int maxLevel = config.levels.Max(l => l.level);
        if (progress.level >= maxLevel)
        {
            Debug.LogWarning($"FacilitiesService.TryUpgradeFacility: '{facilityTypeId}' already at max level.");
            newState = progress;
            return false;
        }

        int nextLevel = progress.level + 1;
        var nextLevelConfig = config.levels.FirstOrDefault(l => l.level == nextLevel);
        if (nextLevelConfig == null)
        {
            Debug.LogError($"FacilitiesService.TryUpgradeFacility: no config found for '{facilityTypeId}' level {nextLevel}.");
            return false;
        }

        // TODO: Add EconomyService.TrySpend(playerId, nextLevelConfig.upgradeCost, 0, "upgrade_facility", out ...)
        // before applying the level-up.

        progress.level = nextLevel;
        SavePlayerFacilityState(playerState);

        newState = progress;
        return true;
    }

    /// <summary>
    /// Returns the full PlayerFacilityState for Progression or any other read-only consumer.
    /// Creates a default file if none exists yet.
    /// </summary>
    public PlayerFacilityState GetPlayerFacilityState(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            playerId = DefaultPlayerId;
        }

        string path = FilePathResolver.GetFacilitiesPath(playerId, PlayerFacilitiesFileName);

        if (!File.Exists(path))
        {
            var newState = CreateDefaultPlayerFacilityState(playerId);
            SavePlayerFacilityState(newState);
            return newState;
        }

        try
        {
            string json = File.ReadAllText(path);
            var state = JsonConvert.DeserializeObject<PlayerFacilityState>(json);

            if (state == null)
            {
                var fallback = CreateDefaultPlayerFacilityState(playerId);
                SavePlayerFacilityState(fallback);
                return fallback;
            }

            state.player_id = string.IsNullOrWhiteSpace(state.player_id) ? playerId : state.player_id;
            state.facilities ??= new Dictionary<string, PlayerFacilityProgress>();

            return state;
        }
        catch (Exception ex)
        {
            Debug.LogError($"FacilitiesService.GetPlayerFacilityState: failed to read state. {ex.Message}");
            var fallback = CreateDefaultPlayerFacilityState(playerId);
            SavePlayerFacilityState(fallback);
            return fallback;
        }
    }

    /// <summary>
    /// Returns a single facility progress record for the requested facility type.
    /// If the player has no saved state for that facility yet, a default level-1 progress record is returned/created.
    /// </summary>
    public PlayerFacilityProgress GetFacilityProgress(string playerId, string facilityTypeId)
    {
        if (!IsValidFacilityType(facilityTypeId))
        {
            Debug.LogWarning($"FacilitiesService.GetFacilityProgress: invalid facilityTypeId '{facilityTypeId}'.");
            return null;
        }

        var state = GetPlayerFacilityState(playerId);
        return GetOrCreateFacilityProgress(state, facilityTypeId);
    }

    /// <summary>
    /// Returns the active effects for the player's current level of the given facility.
    /// This is useful for Progression when applying XP multipliers.
    /// </summary>
    public Dictionary<string, float> GetFacilityEffects(string playerId, string facilityTypeId)
    {
        if (!IsValidFacilityType(facilityTypeId))
        {
            Debug.LogWarning($"FacilitiesService.GetFacilityEffects: invalid facilityTypeId '{facilityTypeId}'.");
            return new Dictionary<string, float>();
        }

        var config = LoadFacilityConfig(facilityTypeId);
        if (config == null || config.levels == null || config.levels.Count == 0)
        {
            return new Dictionary<string, float>();
        }

        var progress = GetFacilityProgress(playerId, facilityTypeId);
        if (progress == null)
        {
            return new Dictionary<string, float>();
        }

        var levelData = config.levels.FirstOrDefault(l => l.level == progress.level);
        return levelData?.effects ?? new Dictionary<string, float>();
    }

    /// <summary>
    /// Returns all current facility effects keyed by facility_type_id.
    /// This is convenient if Progression wants one call instead of three separate calls.
    /// </summary>
    public Dictionary<string, Dictionary<string, float>> GetAllFacilityEffects(string playerId)
    {
        var result = new Dictionary<string, Dictionary<string, float>>();

        foreach (var facilityTypeId in _facilityResourceMap.Keys)
        {
            result[facilityTypeId] = GetFacilityEffects(playerId, facilityTypeId);
        }

        return result;
    }

    /// <summary>
    /// Returns a typed read-only snapshot for Progression or any other consumer
    /// that needs facility levels, effects, and precomputed XP multipliers.
    /// </summary>
    public ProgressionFacilitySnapshot GetProgressionSnapshot(string playerId)
    {
        var normalizedPlayerId = string.IsNullOrWhiteSpace(playerId) ? DefaultPlayerId : playerId;
        var levels = new Dictionary<string, int>();
        var effects = new Dictionary<string, Dictionary<string, float>>();

        foreach (var facilityTypeId in _facilityResourceMap.Keys)
        {
            var progress = GetFacilityProgress(normalizedPlayerId, facilityTypeId);
            levels[facilityTypeId] = progress?.level ?? 1;
            effects[facilityTypeId] = GetFacilityEffects(normalizedPlayerId, facilityTypeId);
        }

        return new ProgressionFacilitySnapshot
        {
            player_id = normalizedPlayerId,
            facility_levels = levels,
            facility_effects = effects,
            match_xp_multiplier = CalculateMatchXpMultiplier(effects),
            training_xp_multiplier = CalculateTrainingXpMultiplier(effects),
            recovery_xp_multiplier = CalculateRecoveryXpMultiplier(effects)
        };
    }

    /// <summary>
    /// Returns the Facilities-defined multiplier that Progression should apply
    /// for a given XP source.
    /// Supported source values include match, training, and recovery.
    /// </summary>
    public float GetProgressionXpMultiplier(string playerId, string source)
    {
        var snapshot = GetProgressionSnapshot(playerId);
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "match" : source.Trim().ToLowerInvariant();

        return normalizedSource switch
        {
            "match" or "match_win" or "match_loss" => snapshot.match_xp_multiplier,
            "training" or "training_session" or "practice" => snapshot.training_xp_multiplier,
            "recovery" or "rehab" => snapshot.recovery_xp_multiplier,
            _ => snapshot.match_xp_multiplier
        };
    }

    public bool IsValidFacilityType(string facilityTypeId)
    {
        return !string.IsNullOrWhiteSpace(facilityTypeId) && _facilityResourceMap.ContainsKey(facilityTypeId);
    }

    private FacilityConfigRoot LoadFacilityConfig(string facilityTypeId)
    {
        if (!IsValidFacilityType(facilityTypeId))
        {
            return null;
        }

        string resourceName = _facilityResourceMap[facilityTypeId];
        TextAsset configAsset = Resources.Load<TextAsset>(resourceName);

        if (configAsset == null)
        {
            Debug.LogError($"FacilitiesService.LoadFacilityConfig: Resources file '{resourceName}' not found.");
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<FacilityConfigRoot>(configAsset.text);
        }
        catch (Exception ex)
        {
            Debug.LogError($"FacilitiesService.LoadFacilityConfig: failed to parse '{resourceName}'. {ex.Message}");
            return null;
        }
    }

    private void SavePlayerFacilityState(PlayerFacilityState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.player_id))
        {
            Debug.LogError("FacilitiesService.SavePlayerFacilityState: invalid state.");
            return;
        }

        state.facilities ??= new Dictionary<string, PlayerFacilityProgress>();

        string path = FilePathResolver.GetFacilitiesPath(state.player_id, PlayerFacilitiesFileName);

        try
        {
            string json = JsonConvert.SerializeObject(state, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"FacilitiesService.SavePlayerFacilityState: failed to save state. {ex.Message}");
        }
    }

    private PlayerFacilityState CreateDefaultPlayerFacilityState(string playerId)
    {
        return new PlayerFacilityState
        {
            player_id = playerId,
            facilities = new Dictionary<string, PlayerFacilityProgress>()
        };
    }

    private PlayerFacilityProgress GetOrCreateFacilityProgress(PlayerFacilityState state, string facilityTypeId)
    {
        state.facilities ??= new Dictionary<string, PlayerFacilityProgress>();

        if (!state.facilities.TryGetValue(facilityTypeId, out var progress) || progress == null)
        {
            progress = new PlayerFacilityProgress
            {
                facility_type_id = facilityTypeId,
                level = 1
            };

            state.facilities[facilityTypeId] = progress;
        }
        else if (string.IsNullOrWhiteSpace(progress.facility_type_id))
        {
            progress.facility_type_id = facilityTypeId;
        }

        return progress;
    }

    private float CalculateMatchXpMultiplier(Dictionary<string, Dictionary<string, float>> allEffects)
    {
        float bonus =
            GetEffectValue(allEffects, "film_room", "PlayerIntelligenceBoost") +
            GetEffectValue(allEffects, "film_room", "GamePlanEffectiveness");

        return 1f + bonus;
    }

    private float CalculateTrainingXpMultiplier(Dictionary<string, Dictionary<string, float>> allEffects)
    {
        float bonus =
            GetEffectValue(allEffects, "weight_room", "PlayerStrengthBoost") +
            GetEffectValue(allEffects, "weight_room", "PlayerConditioningBoost") +
            GetEffectValue(allEffects, "weight_room", "FatigueResistance");

        return 1f + bonus;
    }

    private float CalculateRecoveryXpMultiplier(Dictionary<string, Dictionary<string, float>> allEffects)
    {
        float bonus =
            GetEffectValue(allEffects, "rehab_center", "InjuryRecoveryMultiplier") +
            GetEffectValue(allEffects, "rehab_center", "PlayerHealthBoost") +
            GetEffectValue(allEffects, "rehab_center", "InjuryRiskReduction");

        return 1f + bonus;
    }

    private float GetEffectValue(
        Dictionary<string, Dictionary<string, float>> allEffects,
        string facilityTypeId,
        string effectKey)
    {
        if (!allEffects.TryGetValue(facilityTypeId, out var effects) || effects == null)
        {
            return 0f;
        }

        if (!effects.TryGetValue(effectKey, out var value))
        {
            return 0f;
        }

        if (effectKey.EndsWith("Multiplier"))
        {
            return Mathf.Max(0f, value - 1f);
        }

        return Mathf.Max(0f, value);
    }
}

[System.Serializable]
public class PlayerFacilityState
{
    public string player_id;
    public Dictionary<string, PlayerFacilityProgress> facilities;
}

[System.Serializable]
public class PlayerFacilityProgress
{
    public string facility_type_id;
    public int level;
}

[System.Serializable]
public class FacilityConfigRoot
{
    public List<FacilityLevel> levels;
}

[System.Serializable]
public class FacilityLevel
{
    public int level;
    public int upgradeCost;
    public string descriptor;
    public Dictionary<string, float> effects;
    public Validation validation;
}

[System.Serializable]
public class Validation
{
    public int minCost;
    public int maxCost;
    public Dictionary<string, float> effectCaps;
}

[System.Serializable]
public class ProgressionFacilitySnapshot
{
    public string player_id;
    public Dictionary<string, int> facility_levels;
    public Dictionary<string, Dictionary<string, float>> facility_effects;
    public float match_xp_multiplier;
    public float training_xp_multiplier;
    public float recovery_xp_multiplier;
}
