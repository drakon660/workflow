# State Modeling Proposal for MovieTicketsWorkflow

## Context

This workflow uses the **Decider pattern** for functional event sourcing. The question is how to structure state data that needs to carry through multiple states.

## Current Approach

Each state is a separate record with its own properties:

```csharp
public abstract record MovieTicketState;
public record NoTicket : MovieTicketState;
public record TicketRequestCreated : MovieTicketState
{
    public string TicketId { get; init; }
    public string Name { get; init; }
    // ... all properties
}
public record PaymentConfirmed(string TicketId) : MovieTicketState;  // loses data!
```

**Problem**: Data like `Name`, `FamilyName`, `MovieTitle` is lost when transitioning to `PaymentConfirmed`.

## Options

### Option A: Pure Discriminated Unions (Decider pattern default)

Each state defines all its properties explicitly:

```csharp
public abstract record MovieTicketState;
public record NoTicket : MovieTicketState;

public record TicketRequestCreated : MovieTicketState
{
    public string TicketId { get; init; }
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
}

public record PaymentConfirmed : MovieTicketState
{
    // Copy all data from previous state
    public string TicketId { get; init; }
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
    // Add state-specific data
    public DateTime PaidAt { get; init; }
}

public record TicketPurchasedConfirmed : MovieTicketState
{
    // Copy all + add confirmation code
    public string TicketId { get; init; }
    public string Name { get; init; }
    public string FamilyName { get; init; }
    public int RowNumber { get; init; }
    public int SeatNumber { get; init; }
    public string MovieTitle { get; init; }
    public string RoomName { get; init; }
    public DateTime PaidAt { get; init; }
    public string ConfirmationCode { get; init; }
}
```

**Pros:**
- Type-safe: impossible states are unrepresentable
- Explicit about what data exists in each state
- Follows Decider pattern strictly

**Cons:**
- Repetitive property definitions
- Verbose state transitions in `evolve`

---

### Option B: Two-Level Hierarchy (Recommended)

Common properties in intermediate base class, `NoTicket` stays clean:

```csharp
public abstract record MovieTicketState;

// Initial state - no ticket data yet
public record NoTicket : MovieTicketState;

// Base for all states that have ticket data
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

// Concrete states extend ActiveTicketState
public record TicketRequestCreated : ActiveTicketState;

public record PaymentConfirmed : ActiveTicketState
{
    public DateTime PaidAt { get; init; }
}

public record TicketPurchasedConfirmed : ActiveTicketState
{
    public DateTime PaidAt { get; init; }
    public string ConfirmationCode { get; init; }
}
```

**Pros:**
- No property repetition
- `NoTicket` semantically correct (no ticket = no ticket data)
- Easy transitions using `with` expressions
- Still type-safe with pattern matching

**Cons:**
- Slightly less explicit about each state's exact shape
- Two-level hierarchy adds complexity

**Example transition with `with`:**
```csharp
{ state: TicketRequestCreated s, workflowEvent: Received { Message: MovieTicketPaymentReceived m } }
    => new PaymentConfirmed
    {
        TicketId = s.TicketId,
        Name = s.Name,
        FamilyName = s.FamilyName,
        RowNumber = s.RowNumber,
        SeatNumber = s.SeatNumber,
        MovieTitle = s.MovieTitle,
        RoomName = s.RoomName,
        PaidAt = DateTime.UtcNow
    }
```

---

## Recommendation

**Option B (Two-Level Hierarchy)** is recommended because:

1. Preserves discriminated union benefits (pattern matching, type safety)
2. Reduces boilerplate significantly
3. Makes semantic sense: `NoTicket` vs "active ticket states"
4. Aligns with event sourcing principle that data accumulates over time

## References

- [Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider) - Jeremie Chassaing
- [How To Model Event-Sourced Systems Efficiently](https://www.kurrent.io/blog/how-to-model-event-sourced-systems-efficiently/) - Oskar Dudycz
- [EventSourcing.NetCore](https://github.com/oskardudycz/EventSourcing.NetCore) - Examples and tutorials
