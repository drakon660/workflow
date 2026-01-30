namespace Workflow.Samples.Cinema;

public abstract record MovieTicketInputMessage;

public record PlaceMovieTicketPurchase : MovieTicketInputMessage //bilety
{
    public string TicketId { get; init; }
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }    
}

public record MovieTicketPaymentReceived(string TicketId) : MovieTicketInputMessage;
//placimy

public record MovieTicketsPayed(string TicketId) : MovieTicketInputMessage;

// Seat locking responses from Screening service
public record SeatsLocked(string TicketId) : MovieTicketInputMessage; //miejsca zablokowane
public record SeatLockRejected(string TicketId, string Reason) : MovieTicketInputMessage; //miejsca zajęte