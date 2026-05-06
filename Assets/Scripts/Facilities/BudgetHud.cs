using TMPro;
using UnityEngine;
using System.Globalization;

public class BudgetHud : MonoBehaviour
{
    [Header("UI refs (assign in Inspector)")]
    public TMP_Text budgetText;
    public TMP_Text recoveryBoostText;

    [Header("IDs")]
    public string teamId;

    [Header("Fallback display")]
    public int fallbackBudget = 80000;
    public float fallbackRecoveryBoostPercent = 10f;

    private BudgetHudMiddleware budgetHudMiddleware;
    private bool hasStarted;

    void Awake()
    {
        if (!budgetText) Debug.LogError("[BudgetHud] 'budgetText' is not assigned in Inspector.");
        if (string.IsNullOrWhiteSpace(teamId)) Debug.LogError("[BudgetHud] 'teamId' is empty.");

        budgetHudMiddleware = new BudgetHudMiddleware();
    }

    void OnEnable()
    {
        if (hasStarted)
            Refresh();
    }

    void Start()
    {
        hasStarted = true;
        Refresh();
    }

    public void Refresh()
    {
        var result = budgetHudMiddleware.TryGetBudget(teamId);

        if (!result.Success)
        {
            if (result.MissingBudgetFile)
            {
                ApplyFallbackDisplay();
                return;
            }

            Debug.LogError("[BudgetHud] " + result.Message);

            if (budgetText)
                budgetText.text = "FACILITY BUDGET: [no data]";

            if (recoveryBoostText)
                recoveryBoostText.text = "RECOVERY BOOST: [no data]";

            return;
        }

        if (budgetText)
        {
            var usd = result.Budget.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
            budgetText.text = $"FACILITY BUDGET: {usd}";
        }

        if (recoveryBoostText)
        {
            recoveryBoostText.text = $"RECOVERY BOOST: +{result.RecoveryBoostPercent:0.#}%";
        }
    }

    private void ApplyFallbackDisplay()
    {
        if (budgetText && string.IsNullOrWhiteSpace(budgetText.text))
        {
            var usd = fallbackBudget.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
            budgetText.text = $"FACILITY BUDGET: {usd}";
        }

        if (recoveryBoostText && string.IsNullOrWhiteSpace(recoveryBoostText.text))
            recoveryBoostText.text = $"RECOVERY BOOST: +{fallbackRecoveryBoostPercent:0.#}%";
    }
}
