using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public class BudgetHudMiddleware
{
    private const string BudgetFileName = "teamBudget.json";

    public BudgetHudResult TryGetBudget(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return new BudgetHudResult
            {
                Success = false,
                Message = "TeamId is required."
            };
        }

        string path = Path.Combine(Application.persistentDataPath, BudgetFileName);

        if (!File.Exists(path))
        {
            return new BudgetHudResult
            {
                Success = false,
                MissingBudgetFile = true,
                Message = $"Budget file not found at {path}"
            };
        }

        try
        {
            string json = File.ReadAllText(path);
            var dto = JsonConvert.DeserializeObject<BudgetDto>(json);

            if (dto == null)
            {
                return new BudgetHudResult
                {
                    Success = false,
                    Message = "Budget JSON was null."
                };
            }

            if (dto.TeamId != teamId)
            {
                return new BudgetHudResult
                {
                    Success = false,
                    Message = $"Budget data does not match teamId '{teamId}'."
                };
            }

            return new BudgetHudResult
            {
                Success = true,
                Budget = dto.Budget,
                RecoveryBoostPercent = dto.RecoveryBoostPercent,
                Message = "Budget loaded successfully."
            };
        }
        catch (System.Exception ex)
        {
            return new BudgetHudResult
            {
                Success = false,
                Message = $"Failed to read budget data. {ex.Message}"
            };
        }
    }

    private class BudgetDto
    {
        public string TeamId;
        public decimal Budget;
        public float RecoveryBoostPercent;
    }
}
