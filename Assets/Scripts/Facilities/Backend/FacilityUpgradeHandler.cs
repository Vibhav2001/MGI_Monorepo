using UnityEngine;
using UnityEngine.UI;

public class FacilityUpgradeHandler : MonoBehaviour
{
    [Header("Set these in Inspector")]
    public string playerId = FacilitiesService.DefaultPlayerId;
    public string facilityTypeId; // weight_room, rehab_center, film_room
    public Button upgradeButton;

    private FacilitiesService facilitiesService;

    private void Awake()
    {
        facilitiesService = new FacilitiesService();
    }

    public void OnUpgradeButtonClick()
    {
        Debug.Log($"Upgrade button clicked for facilityTypeId: {facilityTypeId}");

        if (upgradeButton != null)
            upgradeButton.interactable = false;

        bool success = facilitiesService.TryUpgradeFacility(playerId, facilityTypeId, out var newState);

        if (success)
        {
            Debug.Log($"Upgrade Successful: {facilityTypeId} is now level {newState.level}");

            var detailsHandler = FindFirstObjectByType<FacilityDetailsHandler>();

            if (detailsHandler != null)
            {
                detailsHandler.SetIds(playerId, facilityTypeId);
                detailsHandler.RefreshFromLocalState();
                Debug.Log("FacilityDetailsHandler refreshed from local state after upgrade.");
            }
            else
            {
                Debug.LogWarning("FacilityDetailsHandler not found in scene.");
            }
        }
        else
        {
            Debug.LogError($"Upgrade Failed for facilityTypeId: {facilityTypeId}");
        }

        if (upgradeButton != null)
            upgradeButton.interactable = true;
    }
}