# Order Processing - Command Type Matrix

This diagram shows which command types are used in different workflow scenarios.

```mermaid
flowchart LR
    subgraph "Command Types Generated"
        Send[Send Command]
        Schedule[Schedule Command]
        Complete[Complete Command]
        Reply[Reply Command]
    end

    subgraph "Scenarios"
        S1[Order Placed]
        S2[Payment Received]
        S3[Order Shipped]
        S4[Order Delivered]
        S5[Order Cancelled]
        S6[Payment Timeout]
        S7[Check Status]
        S8[Insufficient Inventory]
        S9[Warehouse Response]
    end

    S1 --> Send
    S1 --> Schedule

    S2 --> Send

    S3 --> Send

    S4 --> Send
    S4 --> Complete

    S5 --> Send
    S5 --> Complete

    S6 --> Send
    S6 --> Complete

    S7 --> Reply

    S8 --> Send

    S9 --> Send
    S9 --> Complete

    style Send fill:#e1f5ff
    style Schedule fill:#ffe1f5
    style Complete fill:#90EE90
    style Reply fill:#fff4e1
```

## Command Type Descriptions

- **Send**: Dispatch a command to another service/handler for processing
- **Schedule**: Defer a command for future execution (e.g., timeout handling)
- **Complete**: Mark the workflow instance as finished
- **Reply**: Respond to a query (for async workflow-to-workflow communication)

## Command Usage Patterns

- **Send** is used in almost all scenarios for dispatching work
- **Schedule** is used specifically for timeout handling
- **Complete** marks terminal states (Delivered, Cancelled)
- **Reply** is used for status queries
