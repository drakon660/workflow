namespace Workflow.Samples.Cinema;

using InitiatedBy = Workflow.InitiatedBy<Workflow.Samples.Cinema.MovieTicketInputMessage, Workflow.Samples.Cinema.MovieTicketOutputMessage>;
using Received = Workflow.Received<Workflow.Samples.Cinema.MovieTicketInputMessage, Workflow.Samples.Cinema.MovieTicketOutputMessage>;

public class MovieTicketsWorkflow : Workflow<MovieTicketInputMessage, MovieTicketState, MovieTicketOutputMessage>
{
    public override MovieTicketState InitialState { get; } = new NoTicketState();

    protected override MovieTicketState InternalEvolve(MovieTicketState state,
        WorkflowEvent<MovieTicketInputMessage, MovieTicketOutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            { state: NoTicketState, workflowEvent: InitiatedBy { Message: PlaceMovieTicketPurchase m } } => new
                TicketRequestCreated
                {
                    TicketId = m.WorkflowId,
                    Name = m.Name,
                    FamilyName = m.FamilyName,
                    MovieTitle = m.MovieTitle,
                    RowNumber = m.RowNumber,
                    RoomName = m.RoomName,
                    SeatNumber = m.SeatNumber,
                },

            { state: TicketRequestCreated s, workflowEvent: Received { Message: SeatsLocked m } } => new
                SeatsReserved(m.WorkflowId),

            { state: TicketRequestCreated s, workflowEvent: Received { Message: SeatLockRejected m } } => new
                SeatUnavailable(m.WorkflowId, m.Reason),

            { state: SeatsReserved s, workflowEvent: Received { Message: MovieTicketPaymentReceived m } } => new
                PaymentConfirmed(m.WorkflowId),

            { state: PaymentConfirmed s, workflowEvent: Received { Message: MovieTicketsPayed m } } => new
                TicketPurchasedConfirmed(m.WorkflowId),

            _ => state
        };
    }

    public override IReadOnlyList<WorkflowCommand<MovieTicketOutputMessage>> Decide(MovieTicketInputMessage input,
        MovieTicketState state)
    {
        return (input, state) switch
        {
            { input: PlaceMovieTicketPurchase m, state: NoTicketState } =>
                [Send(new LockSeats(m.WorkflowId))], // first lock seats, don't process payment yet

            { input: SeatsLocked m, state: TicketRequestCreated } =>
                [Send(new MovieTicketProcessPayment(m.WorkflowId))], // seats locked, now process payment

            { input: SeatLockRejected m, state: TicketRequestCreated } =>
                [Send(new NotifySeatUnavailable(m.WorkflowId, m.Reason)), Complete()], // notify user and end workflow

            { input: MovieTicketPaymentReceived m, state: SeatsReserved } =>
                [Send(new ConfirmMovieTicketPurchased(m.WorkflowId))],

            { input: MovieTicketsPayed, state: PaymentConfirmed } =>
                [Complete()],

            _ => []
        };
    }
}
