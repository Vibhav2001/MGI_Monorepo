using UnityEngine;
using TMPro;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.Networking; // GET from backend
using Newtonsoft.Json;        // com.unity.nuget.newtonsoft-json

public enum FacilityType { WeightRoom, Rehab, Film }

public class FacilityDetailsHandler : MonoBehaviour
{
    [Header("UI refs (assign in Inspector)")]
    public TMP_Text currentLevelText;
    public TMP_Text multiplierText;
    public TMP_Text weeklyBoostText;
    public TMP_Text nextBlockText;

    [Header("Local JSON filenames in Resources (no .json)")]
    public string weightRoomRes = "WeightRoom";
    public string rehabRes = "Rehab";
    public string filmRes = "Film";

    [Header("Server config")]
    public string apiBaseUrl = "http://localhost:5263/api";
    public string statusPath = "/facilitystatus";
    public string configPath = "/facilityconfig";

    [Header("IDs (currently reused for local refresh)")]
    public string teamId = FacilitiesService.DefaultPlayerId; // treated as playerId in local mode
    public string playerFacilityId = "weight_room";          // treated as facilityTypeId in local mode

    [Header("Defaults for local-only testing")]
    public FacilityType defaultFacility = FacilityType.WeightRoom;
    [Range(1, 99)] public int currentLevel = 1;

    // Internal state
    readonly Dictionary<FacilityType, FacilityConfigRoot> _configs = new();
    readonly FacilitiesService _facilitiesService = new();

    FacilityType _activeFacility;
    FacilityConfigRoot _activeConfig;

    readonly Dictionary<FacilityType, string> _facilityTypeToId = new()
    {
        { FacilityType.WeightRoom, "weight_room" },
        { FacilityType.Rehab, "rehab_center" },
        { FacilityType.Film, "film_room" }
    };

    readonly Dictionary<string, FacilityType> _facilityIdToType = new()
    {
        { "weight_room", FacilityType.WeightRoom },
        { "rehab_center", FacilityType.Rehab },
        { "film_room", FacilityType.Film }
    };

    void OnEnable()
    {
        LoadAllFromResources();

        if (_configs.TryGetValue(defaultFacility, out var def))
        {
            _activeFacility = defaultFacility;
            _activeConfig = def;
            currentLevel = Mathf.Clamp(currentLevel, 1, _activeConfig.levels.Max(l => l.level));
            BindUI();
        }
        else if (_configs.Count > 0)
        {
            var first = _configs.First();
            _activeFacility = first.Key;
            _activeConfig = first.Value;
            currentLevel = Mathf.Clamp(currentLevel, 1, _activeConfig.levels.Max(l => l.level));
            BindUI();
        }
        else
        {
            Debug.LogWarning("FacilityDetailsHandler: No facility JSONs found in Resources.");
        }
    }

    // -------------------- Public --------------------

    // In local mode:
    // teamId == playerId
    // playerFacilityId == facilityTypeId
    public void SetIds(string team, string pfId)
    {
        teamId = team;
        playerFacilityId = pfId;
    }

    public void ShowWeightRoomFromServer() { StartCoroutine(FetchLevelAndShow(FacilityType.WeightRoom)); }
    public void ShowRehabFromServer() { StartCoroutine(FetchLevelAndShow(FacilityType.Rehab)); }
    public void ShowFilmFromServer() { StartCoroutine(FetchLevelAndShow(FacilityType.Film)); }

    public void RefreshFromServer() { StartCoroutine(FetchLevelAndShow(_activeFacility)); }

    public void ShowWeightRoom() { SwitchFacility(FacilityType.WeightRoom, currentLevel); }
    public void ShowRehab() { SwitchFacility(FacilityType.Rehab, currentLevel); }
    public void ShowFilm() { SwitchFacility(FacilityType.Film, currentLevel); }
    public void SetLevelFromButton(int level) { SetCurrentLevel(level); }

    // -------------------- Server fetch (kept as-is) --------------------

    System.Collections.IEnumerator TryFetchConfigFromServer(FacilityType type)
    {
        if (_configs.ContainsKey(type)) yield break;

        string url = $"{apiBaseUrl.TrimEnd('/')}/{configPath.TrimStart('/')}?facilityType={type}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
            bool isError = req.result != UnityWebRequest.Result.Success;
#else
            bool isError = req.isNetworkError || req.isHttpError;
#endif
            if (isError)
            {
                Debug.LogWarning($"Config GET failed: {req.responseCode} {req.error} ({url}) — will fall back to Resources.");
                yield break;
            }

            FacilityConfigRoot cfg = null;
            try { cfg = JsonConvert.DeserializeObject<FacilityConfigRoot>(req.downloadHandler.text); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Config JSON parse error: {ex.Message} — falling back to Resources.");
            }

            if (cfg != null && cfg.levels != null && cfg.levels.Count > 0)
                _configs[type] = cfg;
        }
    }

    System.Collections.IEnumerator FetchLevelAndShow(FacilityType type)
    {
        yield return StartCoroutine(TryFetchConfigFromServer(type));

        if (!_configs.ContainsKey(type))
        {
            Debug.LogError($"No config for {type}. Ensure server /facilityconfig or place JSON in Resources.");
            yield break;
        }

        string url = $"{apiBaseUrl.TrimEnd('/')}/{statusPath.TrimStart('/')}?teamId={teamId}&playerFacilityId={playerFacilityId}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool isError = req.result != UnityWebRequest.Result.Success;
#else
            bool isError = req.isNetworkError || req.isHttpError;
#endif
            if (isError)
            {
                Debug.LogWarning($"Status GET failed: {req.responseCode} {req.error} ({url})");
                yield break;
            }

            FacilityStatusDto dto = null;
            try { dto = JsonConvert.DeserializeObject<FacilityStatusDto>(req.downloadHandler.text); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Status JSON parse error: {ex.Message}");
            }

            int levelFromServer = (dto != null && dto.CurrentLevel > 0) ? dto.CurrentLevel : currentLevel;
            SwitchFacility(type, levelFromServer);
        }
    }

    // -------------------- Core binding --------------------

    public void SwitchFacility(FacilityType type, int levelFromDb)
    {
        if (!_configs.TryGetValue(type, out var conf))
        {
            Debug.LogError($"SwitchFacility: no config for {type}.");
            return;
        }

        _activeFacility = type;
        _activeConfig = conf;
        SetCurrentLevel(levelFromDb);
    }

    public void SetCurrentLevel(int levelFromDb)
    {
        if (_activeConfig == null) return;
        currentLevel = Mathf.Clamp(levelFromDb, 1, _activeConfig.levels.Max(l => l.level));
        BindUI();
    }

    void BindUI()
    {
        if (_activeConfig == null) return;

        int maxLevel = _activeConfig.levels.Max(l => l.level);
        var cur = _activeConfig.levels.FirstOrDefault(l => l.level == currentLevel);
        var next = _activeConfig.levels.FirstOrDefault(l => l.level == currentLevel + 1);

        string displayName = _activeFacility.ToString();
        if (_activeFacility == FacilityType.WeightRoom) displayName = "Weight";

        if (currentLevelText)
            currentLevelText.text = $"{displayName} • Level {currentLevel} / Max {maxLevel}";

        if (cur != null)
        {
            if (multiplierText) multiplierText.text = FormatMultiplier(cur.effects);
            if (weeklyBoostText) weeklyBoostText.text = "Weekly Boost: " + FormatWeeklyBoost(cur.effects);
        }

        if (nextBlockText)
        {
            if (next != null)
            {
                nextBlockText.text =
                    $"Next: Level {next.level}\n" +
                    $"{FormatMultiplier(next.effects)}\n" +
                    $"Weekly Boost: {FormatWeeklyBoost(next.effects)}\n" +
                    $"Cost: ${next.upgradeCost:n0}";
            }
            else
            {
                nextBlockText.text = "Max level reached";
            }
        }
    }

    // -------------------- Local JSON / FacilitiesService refresh --------------------

    void LoadAllFromResources()
    {
        _configs.Clear();
        TryLoad(FacilityType.WeightRoom, weightRoomRes);
        TryLoad(FacilityType.Rehab, rehabRes);
        TryLoad(FacilityType.Film, filmRes);
    }

    void TryLoad(FacilityType type, string resName)
    {
        if (string.IsNullOrWhiteSpace(resName)) return;

        TextAsset ta = Resources.Load<TextAsset>(resName);
        if (ta == null) return;

        var cfg = SafeParse(ta.text);
        if (cfg != null && cfg.levels != null && cfg.levels.Count > 0)
            _configs[type] = cfg;
        else
            Debug.LogError($"FacilityDetailsHandler: invalid JSON for {type} (no levels). File: {resName}.json");
    }

    FacilityConfigRoot SafeParse(string jsonText)
    {
        try { return JsonConvert.DeserializeObject<FacilityConfigRoot>(jsonText); }
        catch (System.Exception ex)
        {
            Debug.LogError($"FacilityDetailsHandler: JSON parse error: {ex.Message}");
            return null;
        }
    }

    public void RefreshFromLocalState()
    {
        LoadAllFromResources();

        string playerId = string.IsNullOrWhiteSpace(teamId) ? FacilitiesService.DefaultPlayerId : teamId;
        string facilityTypeId = ResolveFacilityTypeId();

        if (!_facilityIdToType.TryGetValue(facilityTypeId, out var facilityType))
        {
            Debug.LogWarning($"RefreshFromLocalState: unknown facilityTypeId '{facilityTypeId}'. Falling back to active facility.");
            facilityType = _activeFacility;
        }

        if (!_configs.ContainsKey(facilityType))
        {
            Debug.LogError($"RefreshFromLocalState: no config loaded for {facilityType}.");
            return;
        }

        var progress = _facilitiesService.GetFacilityProgress(playerId, facilityTypeId);

        if (progress == null)
        {
            Debug.LogWarning("RefreshFromLocalState: no facility progress found. Falling back to current level.");
            SwitchFacility(facilityType, currentLevel);
            return;
        }

        SwitchFacility(facilityType, progress.level);
    }

    string ResolveFacilityTypeId()
    {
        if (!string.IsNullOrWhiteSpace(playerFacilityId) && _facilityIdToType.ContainsKey(playerFacilityId))
        {
            return playerFacilityId;
        }

        if (_facilityTypeToId.TryGetValue(_activeFacility, out var activeId))
        {
            return activeId;
        }

        return _facilityTypeToId[defaultFacility];
    }

    // -------------------- Formatting helpers --------------------

    string FormatMultiplier(Dictionary<string, float> effects)
    {
        if (effects == null || effects.Count == 0) return "-";
        if (effects.TryGetValue("AwarenessMultiplier", out var aware)) return $"Awareness Multiplier: {aware:0.00}x";
        if (effects.TryGetValue("RecoveryMultiplier", out var rec)) return $"Recovery Multiplier: {rec:0.00}x";
        if (effects.TryGetValue("PlayerStrengthBoost", out var str)) return $"Strength Boost: {str * 100f:0.#}%";
        if (effects.TryGetValue("PlayerConditioningBoost", out var c)) return $"Conditioning Boost: {c * 100f:0.#}%";

        var first = effects.First();
        return PrettyKey(first.Key) + ": " + PrettyValue(first.Key, first.Value);
    }

    string FormatWeeklyBoost(Dictionary<string, float> effects)
    {
        if (effects == null) return "-";

        if (effects.TryGetValue("InjuryTimeReductionWeeks", out var weeks))
            return $"-{weeks:0.#} week{(weeks >= 2 ? "s" : "")} injury time";

        var parts = new List<string>();
        if (effects.TryGetValue("PlayerStrengthBoost", out var str)) parts.Add($"+{str * 100f:0.#}% STR");
        if (effects.TryGetValue("PlayerConditioningBoost", out var c)) parts.Add($"+{c * 100f:0.#}% conditioning");
        if (effects.TryGetValue("FatigueResistance", out var fat)) parts.Add($"+{fat * 100f:0.#}% fatigue resist");

        foreach (var kv in effects)
        {
            if (kv.Key is "PlayerStrengthBoost" or "PlayerConditioningBoost" or "FatigueResistance" or "InjuryTimeReductionWeeks")
                continue;
            parts.Add($"{PrettyKey(kv.Key)} {PrettyValue(kv.Key, kv.Value)}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    string PrettyKey(string raw)
    {
        var chars = new List<char>(raw.Length + 4);
        for (int i = 0; i < raw.Length; i++)
        {
            char ch = raw[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLower(raw[i - 1])) chars.Add(' ');
            chars.Add(ch);
        }
        return new string(chars.ToArray());
    }

    string PrettyValue(string key, float v)
    {
        if (key.EndsWith("Multiplier")) return $"{v:0.00}x";

        bool isPercent =
            key.EndsWith("Boost") ||
            key.Contains("Resistance") ||
            key.Contains("Reduction");

        if (isPercent)
        {
            float display = (v <= 1f) ? (v * 100f) : v;
            return $"{display:0.#}%";
        }

        if (key.Contains("Weeks")) return $"{v:0.#} weeks";
        return v.ToString("0.###");
    }

    // -------------------- DTOs / Models --------------------

    [System.Serializable]
    public class FacilityStatusDto
    {
        public string TeamId;
        public string PlayerFacilityId;
        public int CurrentLevel;
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
}