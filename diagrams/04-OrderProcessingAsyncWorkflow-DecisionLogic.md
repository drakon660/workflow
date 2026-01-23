# OrderProcessingAsyncWorkflow - Decision Logic with Branching (DecideAsync Method)

This diagram shows the conditional logic and command generation in the async workflow's `DecideAsync` method.

```mermaid
flowchart TD
    Start([Input: PlaceOrderInputMessage<br/>State: NoOrder])

    Start --> CheckInv{Check Inventory<br/>Available?}

    CheckInv -->|No| NoInv[Commands:<br/>• Send NotifyInsufficientInventory]

    CheckInv -->|Yes| HasInv[Commands:<br/>• Send ProcessPayment<br/>• Send NotifyOrderPlaced<br/>• Schedule PaymentTimeout 15min]

    Input2([Input: InsufficientInventoryInputMessage<br/>State: OrderCreated])

    Input2 --> Req[Commands:<br/>• Send RequestInventoryFromWarehouse]

    Input3([Input: WarehouseInventoryReceivedInputMessage<br/>State: AwaitingWarehouseInventory])

    Input3 --> D2[Commands:<br/>• Send ProcessPayment<br/>• Send NotifyOrderPlaced<br/>• Schedule PaymentTimeout 15min]

    Input4([Input: WarehouseInventoryUnavailableInputMessage<br/>State: AwaitingWarehouseInventory])

    Input4 --> D3[Commands:<br/>• Send NotifyOrderCancelled<br/>• Complete]

    Input5([Input: PaymentReceivedInputMessage<br/>State: OrderCreated])

    Input5 --> D4[Commands:<br/>• Send ShipOrder]

    Input6([Input: OrderShippedInputMessage<br/>State: PaymentConfirmed])

    Input6 --> D5[Commands:<br/>• Send NotifyOrderShipped]

    Input7([Input: OrderDeliveredInputMessage<br/>State: Shipped])

    Input7 --> D6[Commands:<br/>• Send NotifyOrderDelivered<br/>• Complete]

    style CheckInv fill:#ffeb99
    style NoInv fill:#ffd9b3
    style HasInv fill:#e1ffe1
    style D3 fill:#ffcccc
    style D6 fill:#90EE90
    style Req fill:#e1f5ff
```

## Key Decision Points

- **Inventory Check**: The workflow queries an external service to check inventory availability
- **Branching Logic**: Different commands are generated based on the inventory availability
- **Warehouse Fallback**: If local inventory is insufficient, request from warehouse
