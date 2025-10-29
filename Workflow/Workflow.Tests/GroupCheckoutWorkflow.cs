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
                new Pending(m.GroupCheckoutId, m.GuestStayAccountIds.ToDictionary(id => id, _ => GuestStayStatus.Pending)),

            // Guest checkout completed - update status and transition to Finished if all done
            (Pending p, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: GuestCheckedOut m }) =>
                UpdateGuestAndCheckCompletion(p, m.GuestStayAccountId, GuestStayStatus.Completed),

            // Guest checkout failed - update status and transition to Finished if all done
            (Pending p, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: GuestCheckoutFailed m }) =>
                UpdateGuestAndCheckCompletion(p, m.GuestStayAccountId, GuestStayStatus.Failed),

            // Timeout received - transition to Finished
            (Pending, Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> { Message: TimeoutGroupCheckout }) =>
                new Finished(),

            // Unhandled events - return state unchanged
            _ => state
        };
    }

    public override IReadOnlyList<WorkflowCommand<GroupCheckoutOutputMessage>> Decide(GroupCheckoutInputMessage input, GroupCheckoutState state)
    {
        return (input, state) switch
        {
            // Initiate group checkout - send checkout commands for all guests
            (InitiateGroupCheckout m, NotExisting) =>
                m.GuestStayAccountIds
                    .Select(guestId => new Send<GroupCheckoutOutputMessage>(new CheckOut(guestId)) as WorkflowCommand<GroupCheckoutOutputMessage>)
                    .ToList(),

            // Guest checked out - check if all will be completed after this message
            (GuestCheckedOut m, Pending p) =>
                WillBeCompleteAfterProcessing(p, m.GuestStayAccountId, GuestStayStatus.Completed)
                    ? CreateCompletionCommands(p, m.GuestStayAccountId, GuestStayStatus.Completed)
                    : new List<WorkflowCommand<GroupCheckoutOutputMessage>>(),

            // Guest checkout failed - check if all will be completed after this message
            (GuestCheckoutFailed m, Pending p) =>
                WillBeCompleteAfterProcessing(p, m.GuestStayAccountId, GuestStayStatus.Failed)
                    ? CreateCompletionCommands(p, m.GuestStayAccountId, GuestStayStatus.Failed)
                    : new List<WorkflowCommand<GroupCheckoutOutputMessage>>(),

            // Timeout - mark as timed out
            (TimeoutGroupCheckout m, Pending p) =>
                new List<WorkflowCommand<GroupCheckoutOutputMessage>>
                {
                    new Send<GroupCheckoutOutputMessage>(new GroupCheckoutTimedOut(m.GroupCheckoutId, GetPendingGuests(p))),
                    new Complete<GroupCheckoutOutputMessage>()
                },

            _ => new List<WorkflowCommand<GroupCheckoutOutputMessage>>()
        };
    }

    private static GroupCheckoutState UpdateGuestAndCheckCompletion(Pending state, string guestId, GuestStayStatus newStatus)
    {
        var updatedStatuses = UpdateGuestStatus(state.GuestStayAccountStatuses, guestId, newStatus);

        // Check if all guests are now processed
        var allProcessed = updatedStatuses.Values.All(status =>
            status == GuestStayStatus.Completed || status == GuestStayStatus.Failed);

        return allProcessed
            ? new Finished()
            : state with { GuestStayAccountStatuses = updatedStatuses };
    }

    private static Dictionary<string, GuestStayStatus> UpdateGuestStatus(
        Dictionary<string, GuestStayStatus> statuses,
        string guestId,
        GuestStayStatus newStatus)
    {
        var updated = new Dictionary<string, GuestStayStatus>(statuses);
        if (updated.ContainsKey(guestId))
        {
            updated[guestId] = newStatus;
        }
        return updated;
    }

    private static bool WillBeCompleteAfterProcessing(Pending state, string guestId, GuestStayStatus newStatus)
    {
        // Check if all guests will be processed after applying this update
        return state.GuestStayAccountStatuses.All(kvp =>
        {
            if (kvp.Key == guestId)
            {
                // This is the guest being updated - check the new status
                return newStatus == GuestStayStatus.Completed || newStatus == GuestStayStatus.Failed;
            }
            else
            {
                // Other guests - check their current status
                return kvp.Value == GuestStayStatus.Completed || kvp.Value == GuestStayStatus.Failed;
            }
        });
    }

    private static List<WorkflowCommand<GroupCheckoutOutputMessage>> CreateCompletionCommands(Pending state, string currentGuestId, GuestStayStatus currentGuestStatus)
    {
        // Build lists accounting for the current guest's status update
        var completedGuests = state.GuestStayAccountStatuses
            .Where(kvp => kvp.Key == currentGuestId
                ? currentGuestStatus == GuestStayStatus.Completed
                : kvp.Value == GuestStayStatus.Completed)
            .Select(kvp => kvp.Key)
            .ToList();

        var failedGuests = state.GuestStayAccountStatuses
            .Where(kvp => kvp.Key == currentGuestId
                ? currentGuestStatus == GuestStayStatus.Failed
                : kvp.Value == GuestStayStatus.Failed)
            .Select(kvp => kvp.Key)
            .ToList();

        var hasFailures = failedGuests.Any();

        GroupCheckoutOutputMessage outputEvent = hasFailures
            ? new GroupCheckoutFailed(state.GroupCheckoutId, completedGuests, failedGuests)
            : new GroupCheckoutCompleted(state.GroupCheckoutId, completedGuests);

        return new List<WorkflowCommand<GroupCheckoutOutputMessage>>
        {
            new Send<GroupCheckoutOutputMessage>(outputEvent),
            new Complete<GroupCheckoutOutputMessage>()
        };
    }

    private static List<string> GetPendingGuests(Pending state)
    {
        return state.GuestStayAccountStatuses
            .Where(kvp => kvp.Value == GuestStayStatus.Pending)
            .Select(kvp => kvp.Key)
            .ToList();
    }
}

// State types
public abstract record GroupCheckoutState;
public record NotExisting : GroupCheckoutState;
public record Pending(string GroupCheckoutId, Dictionary<string, GuestStayStatus> GuestStayAccountStatuses) : GroupCheckoutState;
public record Finished : GroupCheckoutState;

public enum GuestStayStatus
{
    Pending,
    Completed,
    Failed
}

// Input message types
public abstract record GroupCheckoutInputMessage;
public record InitiateGroupCheckout(string GroupCheckoutId, List<string> GuestStayAccountIds) : GroupCheckoutInputMessage;
public record GuestCheckedOut(string GuestStayAccountId) : GroupCheckoutInputMessage;
public record GuestCheckoutFailed(string GuestStayAccountId, string Reason) : GroupCheckoutInputMessage;
public record TimeoutGroupCheckout(string GroupCheckoutId) : GroupCheckoutInputMessage;

// Output message types
public abstract record GroupCheckoutOutputMessage;
public record CheckOut(string GuestStayAccountId) : GroupCheckoutOutputMessage;
public record GroupCheckoutCompleted(string GroupCheckoutId, List<string> CompletedCheckouts) : GroupCheckoutOutputMessage;
public record GroupCheckoutFailed(string GroupCheckoutId, List<string> CompletedCheckouts, List<string> FailedCheckouts) : GroupCheckoutOutputMessage;
public record GroupCheckoutTimedOut(string GroupCheckoutId, List<string> PendingCheckouts) : GroupCheckoutOutputMessage;
