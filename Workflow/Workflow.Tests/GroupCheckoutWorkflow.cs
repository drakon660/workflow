using System.Collections.Immutable;

namespace Workflow.Tests;

public class GroupCheckoutWorkflow : Workflow<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>
{
    public override GroupCheckoutState InitialState => new NotExisting();

    protected override GroupCheckoutState InternalEvolve(GroupCheckoutState state, WorkflowEvent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            // Workflow initiated
            (NotExisting, InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: InitiateGroupCheckout m }) =>
                new Pending(m.GroupCheckoutId, m.Guests),

            // Guest checkout completed - update status and transition to Finished if all done
            (Pending p, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: GuestCheckedOut m }) =>
               UpdateGuestAndCheckCompletion(p, m.GuestStayAccountId, GuestStayStatus.Completed),
            
            // // Guest checkout failed - update status and transition to Finished if all done
            (Pending p, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: GuestCheckoutFailed m }) =>
                UpdateGuestAndCheckCompletion(p, m.GuestStayAccountId, GuestStayStatus.Failed),

            // Timeout received - transition to Finished
            (Pending, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: TimeoutGroupCheckout }) =>
                new Finished(),

            (Pending p, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: GetCheckoutStatus m }) => state,
            
            // Unhandled events - return state unchanged
            _ => throw new InvalidOperationException($"{workflowEvent} not supported by {state}")
        };
    }

    private IReadOnlyList<WorkflowCommand<GroupCheckoutOutputMessage>> GenerateCheckoutCommands(IReadOnlyList<Guest> guests)
    {
        return guests.Select(x => Send(new CheckOut(x.Id))).ToList();
    }
    
    private static List<WorkflowCommand<GroupCheckoutOutputMessage>> EmptyCommands => []; 
    
    public override IReadOnlyList<WorkflowCommand<GroupCheckoutOutputMessage>> Decide(GroupCheckoutInputMessage input, GroupCheckoutState state)
    {
        return (input, state) switch
        {
            // Initiate group checkout - send checkout commands for all guests
            (InitiateGroupCheckout m, NotExisting) => GenerateCheckoutCommands(m.Guests),

            // Guest checked out - check if all will be completed after this message
           (GuestCheckedOut m, Pending p) => 
                 WillBeCompleteAfterProcessing(p, m.GuestStayAccountId, GuestStayStatus.Completed)
                     ? CreateCompletionCommands(p, m.GuestStayAccountId, GuestStayStatus.Completed)
                     : EmptyCommands,
            
             // Guest checkout failed - check if all will be completed after this message
             (GuestCheckoutFailed m, Pending p) =>  
                 WillBeCompleteAfterProcessing(p, m.GuestStayAccountId, GuestStayStatus.Failed)
                     ? CreateCompletionCommands(p, m.GuestStayAccountId, GuestStayStatus.Failed)
                     : EmptyCommands,
            
             // Timeout - mark as timed out
             (TimeoutGroupCheckout m, Pending p) =>
                 [
                     Send(new GroupCheckoutTimedOut(m.GroupCheckoutId, GetPendingGuests(p))),
                     Complete()
                 ],

            (GetCheckoutStatus m, Pending p) => [
                Reply(new CheckoutStatus(
                    GroupCheckoutId: p.GroupCheckoutId,
                    Status: "Pending",
                    TotalGuests: p.Guests.Count,
                    CompletedGuests: p.Guests.Count(g => g.GuestStayStatus == GuestStayStatus.Completed),
                    FailedGuests: p.Guests.Count(g => g.GuestStayStatus == GuestStayStatus.Failed),
                    PendingGuests: p.Guests.Count(g => g.GuestStayStatus == GuestStayStatus.Pending),
                    Guests: p.Guests.Select(g => new GuestStatus(g.Id, g.GuestStayStatus.ToString())).ToList()
                ))
            ],
            
            _ => EmptyCommands
        };
    }

    private GroupCheckoutState UpdateGuestAndCheckCompletion(Pending state, string guestId, GuestStayStatus newStatus)
    {
        // Create new list with updated guest (immutable)
        var updatedGuests = state.Guests.Select(guest =>
            guest.Id == guestId
                ? guest with { GuestStayStatus = newStatus }
                : guest
        ).ToList();

        // Check if all guests are now processed
        var allProcessed = updatedGuests.All(x =>
            x.GuestStayStatus is GuestStayStatus.Completed or GuestStayStatus.Failed);

        return allProcessed
            ? new Finished()
            : state with { Guests = updatedGuests };
    }

    private bool WillBeCompleteAfterProcessing(Pending state, string guestId, GuestStayStatus newStatus)
    {
        // Check if all guests will be processed after applying this update
        return state.Guests.All(kvp =>
        {
            if (kvp.Id == guestId)
            {
                // This is the guest being updated - check the new status
                return newStatus is GuestStayStatus.Completed or GuestStayStatus.Failed;
            }

            // Other guests - check their current status
            return kvp.GuestStayStatus is GuestStayStatus.Completed or GuestStayStatus.Failed;
        });
    }

    private List<WorkflowCommand<GroupCheckoutOutputMessage>> CreateCompletionCommands(Pending state, string currentGuestId, GuestStayStatus currentGuestStatus)
    {
        // Build lists accounting for the current guest's status update
        var completedGuests = state.Guests
            .Where(kvp => kvp.Id == currentGuestId
                ? currentGuestStatus == GuestStayStatus.Completed
                : kvp.GuestStayStatus == GuestStayStatus.Completed)
            .Select(kvp => kvp.Id)
            .ToList();

        var failedGuests = state.Guests
            .Where(kvp => kvp.Id == currentGuestId
                ? currentGuestStatus == GuestStayStatus.Failed
                : kvp.GuestStayStatus == GuestStayStatus.Failed)
            .Select(kvp => kvp.Id)
            .ToList();

        var hasFailures = failedGuests.Any();

        GroupCheckoutOutputMessage outputEvent = hasFailures
            ? new GroupCheckoutFailed(state.GroupCheckoutId, completedGuests, failedGuests)
            : new GroupCheckoutCompleted(state.GroupCheckoutId, completedGuests);

        return
        [
            Send(outputEvent),
            Complete()
        ];
    }
    
    private List<string> GetPendingGuests(Pending state)
    {
        return state.Guests
            .Where(kvp => kvp.GuestStayStatus == GuestStayStatus.Pending)
            .Select(kvp => kvp.Id)
            .ToList();
    }
}

public record Guest(string Id, GuestStayStatus GuestStayStatus = GuestStayStatus.Pending);

// State types
public abstract record GroupCheckoutState;
public record NotExisting : GroupCheckoutState;
public record Pending(string GroupCheckoutId, IReadOnlyList<Guest> Guests) : GroupCheckoutState;
public record Finished : GroupCheckoutState;

public enum GuestStayStatus
{
    Pending,
    Completed,
    Failed
}

// Input message types
public abstract record GroupCheckoutInputMessage;
public record InitiateGroupCheckout(string GroupCheckoutId, IReadOnlyList<Guest> Guests) : GroupCheckoutInputMessage;
public record GuestCheckedOut(string GuestStayAccountId) : GroupCheckoutInputMessage;
public record GuestCheckoutFailed(string GuestStayAccountId, string Reason) : GroupCheckoutInputMessage;
public record TimeoutGroupCheckout(string GroupCheckoutId) : GroupCheckoutInputMessage;

public record GetCheckoutStatus(string GroupCheckoutId) : GroupCheckoutInputMessage;

// Output message types
public abstract record GroupCheckoutOutputMessage;
public record CheckOut(string GuestStayAccountId) : GroupCheckoutOutputMessage;
public record GroupCheckoutCompleted(string GroupCheckoutId, List<string> CompletedCheckouts) : GroupCheckoutOutputMessage;
public record GroupCheckoutFailed(string GroupCheckoutId, List<string> CompletedCheckouts, List<string> FailedCheckouts) : GroupCheckoutOutputMessage;
public record GroupCheckoutTimedOut(string GroupCheckoutId, List<string> PendingCheckouts) : GroupCheckoutOutputMessage;
public record GuestStatus(string GuestId, string Status);
public record CheckoutStatus(
    string GroupCheckoutId,
    string Status,  // "Pending", "Completed", "Failed"
    int TotalGuests,
    int CompletedGuests,
    int FailedGuests,
    int PendingGuests,
    List<GuestStatus> Guests
) : GroupCheckoutOutputMessage;
