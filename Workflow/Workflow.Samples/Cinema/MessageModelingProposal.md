# Message Modeling Proposal for MovieTicketsWorkflow

## Context

This document discusses whether input/output messages should follow a hierarchy pattern similar to the state modeling (Option B with `ActiveTicketState`).

## Current Approach

Messages are flat records with minimal data:

```csharp
// Input messages
public abstract record MovieTicketInputMessage;
public record PlaceMovieTicketPurchase : MovieTicketInputMessage
{
    public string TicketId { get; init; }
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
}
public record SeatsLocked(string TicketId) : MovieTicketInputMessage;
public record SeatLockRejected(string TicketId, string Reason) : MovieTicketInputMessage;
public record MovieTicketPaymentReceived(string TicketId) : MovieTicketInputMessage;
public record MovieTicketsPayed(string TicketId) : MovieTicketInputMessage;

// Output messages
public abstract record MovieTicketOutputMessage;
public record LockSeats(string TicketId) : MovieTicketOutputMessage;
public record MovieTicketProcessPayment(string TicketId) : MovieTicketOutputMessage;
public record ConfirmMovieTicketPurchased(string TicketId) : MovieTicketOutputMessage;
public record NotifySeatUnavailable(string TicketId, string Reason) : MovieTicketOutputMessage;
```

## Why Messages Are Different From States

**States** need hierarchy because:
- Data accumulates and carries forward through the workflow lifecycle
- Each state transition should preserve previous data
- The state represents the "current snapshot" of the workflow

**Messages** are different because:
- Each message is a discrete event or command
- They don't need to carry data forward (that's the state's job)
- They only contain data relevant to that specific event
- The `TicketId` acts as a correlation ID to link the message to the workflow instance

## Options

### Option A: Keep Flat (Recommended)

Keep messages as flat records. Each message contains only what it needs:

```csharp
public record SeatsLocked(string TicketId) : MovieTicketInputMessage;
public record SeatLockRejected(string TicketId, string Reason) : MovieTicketInputMessage;
```

**Pros:**
- Simple and explicit
- Each message is self-documenting
- No unnecessary abstraction
- Follows CQRS/Event Sourcing conventions

**Cons:**
- `TicketId` repeated in each message (minor)

---

### Option B: Base Class for Correlated Messages

Add a base class for messages that have a `TicketId`:

```csharp
// Input messages
public abstract record MovieTicketInputMessage;

public abstract record CorrelatedTicketInputMessage : MovieTicketInputMessage
{
    public string TicketId { get; init; }
}

public record PlaceMovieTicketPurchase : CorrelatedTicketInputMessage
{
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
}

public record SeatsLocked : CorrelatedTicketInputMessage;
public record SeatLockRejected : CorrelatedTicketInputMessage
{
    public string Reason { get; init; }
}
public record MovieTicketPaymentReceived : CorrelatedTicketInputMessage;
public record MovieTicketsPayed : CorrelatedTicketInputMessage;

// Output messages
public abstract record MovieTicketOutputMessage;

public abstract record CorrelatedTicketOutputMessage : MovieTicketOutputMessage
{
    public string TicketId { get; init; }
}

public record LockSeats : CorrelatedTicketOutputMessage;
public record MovieTicketProcessPayment : CorrelatedTicketOutputMessage;
public record ConfirmMovieTicketPurchased : CorrelatedTicketOutputMessage;
public record NotifySeatUnavailable : CorrelatedTicketOutputMessage
{
    public string Reason { get; init; }
}
```

**Pros:**
- Consistent with state hierarchy pattern
- `TicketId` defined once per hierarchy
- Can add shared behavior/validation in base class

**Cons:**
- Added complexity for little benefit
- Messages are typically simple DTOs that don't need inheritance
- Harder to see at a glance what data each message carries

---

## Recommendation

**Option A (Keep Flat)** is recommended because:

1. Messages are fundamentally different from states - they're discrete events, not accumulated snapshots
2. The repetition of `TicketId` is minimal and explicit
3. Simpler code is easier to maintain
4. Follows established patterns in CQRS/Event Sourcing communities
5. Each message is self-contained and easy to understand

The state hierarchy (Option B in StateModelingProposal.md) makes sense because states need to preserve data across transitions. Messages don't have this requirement.

## Summary

| Concern | States | Messages |
|---------|--------|----------|
| Data accumulation | Yes - data carries forward | No - each message is independent |
| Hierarchy benefit | High - reduces duplication | Low - minimal duplication |
| Recommendation | Option B (hierarchy) | Option A (flat) |
