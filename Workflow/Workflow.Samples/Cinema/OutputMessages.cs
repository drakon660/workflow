namespace Workflow.Samples.Cinema;

public abstract record MovieTicketOutputMessage;

public record MovieTicketProcessPayment(string TicketId) : MovieTicketOutputMessage; //zapłać
public record LockSeats(string TicketId) : MovieTicketOutputMessage; //zablokuj
public record ConfirmMovieTicketPurchased(string TicketId) : MovieTicketOutputMessage;
//wyslij

public record NotifySeatUnavailable(string TicketId, string Reason) : MovieTicketOutputMessage; //powiadom o niedostępności