# OrderProcessingWorkflow - State Transitions (InternalEvolve)

This diagram shows the state transitions based on the `InternalEvolve` method of the synchronous OrderProcessingWorkflow.

```mermaid
flowchart TD
    Start([Start]) --> NoOrder[NoOrder]

    NoOrder -->|InitiatedBy<br/>PlaceOrderInputMessage| OrderCreated[OrderCreated]

    OrderCreated -->|Received<br/>PaymentReceivedInputMessage| PaymentConfirmed[PaymentConfirmed]

    OrderCreated -->|Received<br/>OrderCancelledInputMessage| Cancelled[Cancelled]

    OrderCreated -->|Received<br/>PaymentTimeoutInputMessage| Cancelled

    PaymentConfirmed -->|Received<br/>OrderShippedInputMessage| Shipped[Shipped]

    Shipped -->|Received<br/>OrderDeliveredInputMessage| Delivered[Delivered]

    Delivered --> End([End])
    Cancelled --> End

    style NoOrder fill:#e1f5ff
    style OrderCreated fill:#fff4e1
    style PaymentConfirmed fill:#e1ffe1
    style Shipped fill:#ffe1f5
    style Delivered fill:#90EE90
    style Cancelled fill:#ffcccc
```

## State Descriptions

- **NoOrder**: Initial state, no order exists yet
- **OrderCreated**: Order has been placed, awaiting payment
- **PaymentConfirmed**: Payment received successfully
- **Shipped**: Order has been shipped with tracking number
- **Delivered**: Order successfully delivered (terminal state)
- **Cancelled**: Order cancelled due to timeout or customer request (terminal state)
