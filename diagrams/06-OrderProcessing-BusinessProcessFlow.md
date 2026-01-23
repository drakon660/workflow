# Order Processing - Business Process Flow

This diagram shows the workflow from a business perspective with actions and decisions.

```mermaid
flowchart TD
    Start([Customer Places Order])

    Start --> Act1[Actions:<br/>✓ Process Payment<br/>✓ Notify Customer<br/>✓ Set 15min Timeout]

    Act1 --> Wait1{Wait for<br/>Payment}

    Wait1 -->|Payment Received| Act2[Action:<br/>✓ Ship Order]

    Wait1 -->|Manual Cancellation| Act3[Actions:<br/>✓ Notify Cancellation<br/>✓ Complete Workflow]

    Wait1 -->|Timeout After 15min| Act4[Actions:<br/>✓ Notify Cancellation<br/>✓ Complete Workflow]

    Act2 --> Wait2{Wait for<br/>Shipping}

    Wait2 -->|Order Shipped| Act5[Action:<br/>✓ Notify Shipped Status]

    Act5 --> Wait3{Wait for<br/>Delivery}

    Wait3 -->|Order Delivered| Act6[Actions:<br/>✓ Notify Delivered<br/>✓ Complete Workflow]

    Act3 --> End([Workflow Complete])
    Act4 --> End
    Act6 --> End

    style Act1 fill:#e1f5ff
    style Act2 fill:#e1ffe1
    style Act3 fill:#ffcccc
    style Act4 fill:#ffcccc
    style Act5 fill:#ffe1f5
    style Act6 fill:#90EE90
    style Wait1 fill:#ffeb99
    style Wait2 fill:#ffeb99
    style Wait3 fill:#ffeb99
```

## Business Flow Description

1. **Order Placement**: Customer initiates order, system processes payment and sets timeout
2. **Payment Wait**: Three possible outcomes:
   - Payment received → proceed to shipping
   - Manual cancellation → cancel order
   - Timeout (15 minutes) → auto-cancel order
3. **Shipping Wait**: System waits for shipping confirmation
4. **Delivery Wait**: System waits for delivery confirmation
5. **Completion**: Workflow completes successfully or with cancellation

## Key Business Rules

- **15-minute payment timeout**: Orders not paid within 15 minutes are automatically cancelled
- **Customer notifications**: Customers are notified at each major step
- **Graceful cancellation**: Orders can be cancelled at the OrderCreated state
