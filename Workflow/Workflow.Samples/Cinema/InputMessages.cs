namespace Workflow.Samples.Cinema;

public abstract record MovieTicketInputMessage : IWorkflowInput
{
    public required string WorkflowId { get; init; }
}

public record PlaceMovieTicketPurchase : MovieTicketInputMessage //bilety
{
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
}

public record MovieTicketPaymentReceived : MovieTicketInputMessage;
//placimy

public record MovieTicketsPayed : MovieTicketInputMessage;

// Seat locking responses from Screening service
public record SeatsLocked : MovieTicketInputMessage; //miejsca zablokowane
public record SeatLockRejected(string Reason) : MovieTicketInputMessage; //miejsca zajęte
