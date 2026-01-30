namespace Workflow.Samples.Cinema;

public abstract record MovieTicketState;

public record NoTicketState :  MovieTicketState; //nie ma biletu

public abstract record ActiveTicketState : MovieTicketState
{
    public string TicketId { get; init; }
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
}

public record TicketRequestCreated : ActiveTicketState; //kupujemy

public record SeatsReserved(string TicketId) : ActiveTicketState; //miejsca zarezerwowane, czekamy na płatność
public record SeatUnavailable(string TicketId, string Reason) : ActiveTicketState; //miejsca niedostępne

public record PaymentConfirmed(string TicketId) : ActiveTicketState; //potwierdzenie płatności
public record TicketPurchasedConfirmed(string TicketId) : ActiveTicketState; //dostalismy