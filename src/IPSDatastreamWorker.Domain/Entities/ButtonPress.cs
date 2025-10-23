using IPSDatastreamWorker.Domain.Common;

namespace IPSDatastreamWorker.Domain.Entities;

public class ButtonPress : BaseEntity
{
    public string SessionId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public int? FloorIndex { get; set; }
    public bool IsSynced { get; set; } = true;
}

public static class ButtonAction
{
    public const string EnteredRestaurantBuilding = "ENTERED_RESTAURANT_BUILDING";
    public const string EnteredElevator = "ENTERED_ELEVATOR";
    public const string ClimbingStairs = "CLIMBING_STAIRS";
    public const string GoingUpInLift = "GOING_UP_IN_LIFT";
    public const string ReachedRestaurantCorridor = "REACHED_RESTAURANT_CORRIDOR";
    public const string ReachedRestaurant = "REACHED_RESTAURANT";
    public const string LeftRestaurant = "LEFT_RESTAURANT";
    public const string ComingDownStairs = "COMING_DOWN_STAIRS";
    public const string LeftRestaurantBuilding = "LEFT_RESTAURANT_BUILDING";
    public const string ReachedSocietyGate = "REACHED_SOCIETY_GATE";
    public const string EnteredDeliveryBuilding = "ENTERED_DELIVERY_BUILDING";
    public const string AnotherFloorInBuilding = "ANOTHER_FLOOR_IN_BUILDING";
    public const string ExitingBuilding = "EXITING_BUILDING";
    public const string BackToGroundFloor = "BACK_TO_GROUND_FLOOR";
    public const string AnotherBuildingInSociety = "ANOTHER_BUILDING_IN_SOCIETY";
    public const string ReachedDeliveryCorridor = "REACHED_DELIVERY_CORRIDOR";
    public const string ReachedDoorstep = "REACHED_DOORSTEP";
    public const string LeftDoorstep = "LEFT_DOORSTEP";
    public const string GoingDownInLift = "GOING_DOWN_IN_LIFT";
    public const string LeavingSociety = "LEAVING_SOCIETY";
    public const string LeftDeliveryBuilding = "LEFT_DELIVERY_BUILDING";
}

