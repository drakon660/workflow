# OrderProcessingAsyncWorkflow - State Transitions (InternalEvolve)

This diagram shows the state transitions based on the `InternalEvolve` method of the asynchronous OrderProcessingWorkflow with inventory checking.

```mermaid
flowchart TD
    Start([Start]) --> NoOrder[NoOrder]

    NoOrder -->|InitiatedBy<br/>PlaceOrderInputMessage| OrderCreated[OrderCreated]

    OrderCreated -->|Received<br/>InsufficientInventoryInputMessage| AwaitingWarehouseInventory[AwaitingWarehouseInventory]

    OrderCreated -->|Received<br/>PaymentReceivedInputMessage| PaymentConfirmed[PaymentConfirmed]

    OrderCreated -->|Received<br/>OrderCancelledInputMessage| Cancelled[Cancelled]

    OrderCreated -->|Received<br/>PaymentTimeoutInputMessage| Cancelled

    AwaitingWarehouseInventory -->|Received<br/>WarehouseInventoryReceivedInputMessage| PaymentConfirmed

    AwaitingWarehouseInventory -->|Received<br/>WarehouseInventoryUnavailableInputMessage| Cancelled

    PaymentConfirmed -->|Received<br/>OrderShippedInputMessage| Shipped[Shipped]

    Shipped -->|Received<br/>OrderDeliveredInputMessage| Delivered[Delivered]

    Delivered --> End([End])
    Cancelled --> End

    style NoOrder fill:#e1f5ff
    style OrderCreated fill:#fff4e1
    style AwaitingWarehouseInventory fill:#ffd9b3
    style PaymentConfirmed fill:#e1ffe1
    style Shipped fill:#ffe1f5
    style Delivered fill:#90EE90
    style Cancelled fill:#ffcccc
```

## State Descriptions

- **NoOrder**: Initial state, no order exists yet
- **OrderCreated**: Order has been placed, checking inventory
- **AwaitingWarehouseInventory**: Waiting for warehouse to confirm inventory availability
- **PaymentConfirmed**: Payment received successfully, ready to ship
- **Shipped**: Order has been shipped with tracking number
- **Delivered**: Order successfully delivered (terminal state)
- **Cancelled**: Order cancelled (timeout, customer request, or inventory unavailable) (terminal state)
