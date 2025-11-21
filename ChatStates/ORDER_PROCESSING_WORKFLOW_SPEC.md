# Order Processing Workflow - Requirements

**Last Updated:** 2025-11-21 (Added Reply command for queries)

---

## Overview

A simple, linear workflow to test core workflow engine mechanics: **Decide**, **Evolve**, and **Translate**.

**Focus:** Test the workflow engine, not business complexity

**What We're Testing:**
- State transitions (Evolve)
- Command generation (Decide)
- Event generation (Translate)
- Linear progression through states
- Basic cancellation logic
- Timeout with Schedule command
- Query pattern with Reply command

---

## State Machine

```
NotExisting
    ↓ (PlaceOrder)
OrderCreated
    ↓ (PaymentReceived)
PaymentConfirmed
    ↓ (OrderShipped)
Shipped
    ↓ (OrderDelivered)
Delivered → Complete

Cancellation path:
OrderCreated → (CancelOrder or PaymentTimeout) → Cancelled → Complete
```

---

## States (Simplified)

### 1. NotExisting
Empty initial state

---

### 2. OrderCreated
**Properties:**
- OrderId (string)

---

### 3. PaymentConfirmed
**Properties:**
- OrderId (string)

---

### 4. Shipped
**Properties:**
- OrderId (string)
- TrackingNumber (string)

---

### 5. Delivered
**Properties:**
- OrderId (string)
- TrackingNumber (string)

---

### 6. Cancelled
**Properties:**
- OrderId (string)
- Reason (string)

---

## Input Messages (Simplified)

### 1. PlaceOrder
- OrderId (string)

### 2. PaymentReceived
- OrderId (string)

### 3. OrderShipped
- OrderId (string)
- TrackingNumber (string)

### 4. OrderDelivered
- OrderId (string)

### 5. CancelOrder
- OrderId (string)
- Reason (string)

### 6. PaymentTimeout
- OrderId (string)

### 7. CheckOrderState
- OrderId (string)
- *Note: Query message - doesn't change state*

---

## Output Commands (Simplified)

### 1. ProcessPayment
- OrderId (string)

### 2. NotifyOrderPlaced
- OrderId (string)

### 3. ShipOrder
- OrderId (string)

### 4. NotifyOrderShipped
- OrderId (string)
- TrackingNumber (string)

### 5. NotifyOrderDelivered
- OrderId (string)

### 6. NotifyOrderCancelled
- OrderId (string)
- Reason (string)

### 7. PaymentTimeout
- OrderId (string)
- *Note: This is scheduled and then received back as input*

### 8. OrderProcessingStatus
- OrderId (string)
- Status (string)
- *Note: Reply message for queries*

---

## Business Logic (What Commands to Generate)

### When PlaceOrder (NotExisting state)
**Generate:**
1. Send ProcessPayment
2. Send NotifyOrderPlaced
3. Schedule PaymentTimeout (after 15 minutes)

---

### When PaymentReceived (OrderCreated state)
**Generate:**
1. Send ShipOrder

---

### When OrderShipped (PaymentConfirmed state)
**Generate:**
1. Send NotifyOrderShipped

---

### When OrderDelivered (Shipped state)
**Generate:**
1. Send NotifyOrderDelivered
2. Complete workflow

---

### When CancelOrder (OrderCreated state)
**Generate:**
1. Send NotifyOrderCancelled
2. Complete workflow

---

### When PaymentTimeout (OrderCreated state)
**Generate:**
1. Send NotifyOrderCancelled (with reason "timeout")
2. Complete workflow

---

### When CancelOrder (any other state)
**Generate:**
- No commands (ignore cancellation)

---

### When CheckOrderState (any state)
**Generate:**
1. Reply OrderProcessingStatus with current state
   - NoOrder → Status: "NotExisting"
   - OrderCreated → Status: "OrderCreated"
   - PaymentConfirmed → Status: "PaymentConfirmed"
   - Shipped → Status: "Shipped"
   - Delivered → Status: "Delivered"
   - Cancelled → Status: "Cancelled: {reason}"

---

## State Transitions (Evolve Requirements)

### PlaceOrder → OrderCreated
- Create OrderCreated state with OrderId

### PaymentReceived → PaymentConfirmed
- Create PaymentConfirmed state with OrderId

### OrderShipped → Shipped
- Create Shipped state with OrderId and TrackingNumber

### OrderDelivered → Delivered
- Create Delivered state with OrderId and TrackingNumber

### CancelOrder (from OrderCreated) → Cancelled
- Create Cancelled state with OrderId and Reason

### PaymentTimeout (from OrderCreated) → Cancelled
- Create Cancelled state with OrderId and Reason="timeout"

### Events that don't change state
- Began
- Sent
- Scheduled
- Published
- Replied
- Completed
- Received(CheckOrderState) - Query events don't mutate state

---

## Test Scenarios

### Test 1: Happy Path
**Flow:**
1. PlaceOrder → OrderCreated
2. PaymentReceived → PaymentConfirmed
3. OrderShipped → Shipped
4. OrderDelivered → Delivered → Complete

**Verify:**
- All state transitions occurred
- Commands: ProcessPayment, NotifyOrderPlaced, Schedule, ShipOrder, NotifyOrderShipped, NotifyOrderDelivered, Complete

---

### Test 2: Cancel Before Payment
**Flow:**
1. PlaceOrder → OrderCreated
2. CancelOrder → Cancelled → Complete

**Verify:**
- Cancelled state created
- Commands: ProcessPayment, NotifyOrderPlaced, Schedule, NotifyOrderCancelled, Complete

---

### Test 3: Payment Timeout
**Flow:**
1. PlaceOrder → OrderCreated
2. PaymentTimeout → Cancelled → Complete

**Verify:**
- Cancelled state with reason="timeout"
- Commands: ProcessPayment, NotifyOrderPlaced, Schedule, NotifyOrderCancelled, Complete

---

### Test 4: Cannot Cancel After Payment
**Flow:**
1. PlaceOrder → OrderCreated
2. PaymentReceived → PaymentConfirmed
3. CancelOrder → No effect, still PaymentConfirmed

**Verify:**
- State remains PaymentConfirmed
- No commands generated from CancelOrder

---

### Test 5: Decide Method Tests
**Verify Decide generates correct commands for:**
- (PlaceOrder, NotExisting) → [Send ProcessPayment, Send NotifyOrderPlaced, Schedule PaymentTimeout]
- (PaymentReceived, OrderCreated) → [Send ShipOrder]
- (OrderShipped, PaymentConfirmed) → [Send NotifyOrderShipped]
- (OrderDelivered, Shipped) → [Send NotifyOrderDelivered, Complete]
- (CancelOrder, OrderCreated) → [Send NotifyOrderCancelled, Complete]
- (PaymentTimeout, OrderCreated) → [Send NotifyOrderCancelled, Complete]
- (CancelOrder, PaymentConfirmed) → []
- (CheckOrderState, any state) → [Reply OrderProcessingStatus]

---

### Test 6: Evolve Method Tests
**Verify Evolve creates correct states for:**
- (NotExisting, InitiatedBy PlaceOrder) → OrderCreated(orderId)
- (OrderCreated, Received PaymentReceived) → PaymentConfirmed(orderId)
- (PaymentConfirmed, Received OrderShipped) → Shipped(orderId, trackingNumber)
- (Shipped, Received OrderDelivered) → Delivered(orderId, trackingNumber)
- (OrderCreated, Received CancelOrder) → Cancelled(orderId, reason)
- (OrderCreated, Received PaymentTimeout) → Cancelled(orderId, "timeout")

---

### Test 7: Translate Method Tests
**Verify Translate generates correct events:**
- When workflow begins (begins=true)
- When workflow continues (begins=false)
- Events include: Began, InitiatedBy, Received, Sent, Scheduled, Completed

---

### Test 8: Query with Reply
**Flow:**
1. PlaceOrder → OrderCreated
2. CheckOrderState → Reply with OrderProcessingStatus("OrderCreated")
3. State remains OrderCreated (unchanged)

**Verify:**
- Reply command generated with correct status
- State unchanged after query
- Events: Received(CheckOrderState), Replied(OrderProcessingStatus)

---

### Test 9: Reply in Different States
**Verify Reply returns correct status for each state:**
- CheckOrderState in NoOrder → Status: "NotExisting"
- CheckOrderState in OrderCreated → Status: "OrderCreated"
- CheckOrderState in PaymentConfirmed → Status: "PaymentConfirmed"
- CheckOrderState in Shipped → Status: "Shipped"
- CheckOrderState in Delivered → Status: "Delivered"
- CheckOrderState in Cancelled → Status: "Cancelled: {reason}"

---

## Success Criteria

✅ All 6 states implemented (minimal properties)
✅ All 7 input messages implemented (minimal properties)
✅ All 8 output messages implemented (minimal properties)
✅ Decide method generates correct commands (including Reply)
✅ Evolve method performs correct state transitions
✅ Reply command doesn't change state
✅ All 9 test scenarios pass
✅ Focus on testing workflow mechanics, not business logic

---

## Key Simplifications

1. **Minimal Properties** - Only what's needed to track state
2. **No Validation** - Focus on workflow mechanics
3. **Simplified Commands** - One notification per event
4. **No Complex Business Rules** - Just test Decide/Evolve/Translate
5. **Linear Flow** - Easy to follow and test

---

## What We're NOT Testing

❌ Complex validation logic
❌ Multiple items in orders
❌ Monetary calculations
❌ Address validation
❌ Complex business rules
❌ External system integration
❌ Refund processing
❌ Multiple command types per event

**Focus:** Prove the workflow engine works correctly

---

**Status:** Simplified Requirements - Implementation In Progress
**Estimated Effort:** 1-2 hours
**Purpose:** Test Decide, Evolve, Translate methods with minimal complexity
**Latest Addition:** Reply command for querying workflow state
