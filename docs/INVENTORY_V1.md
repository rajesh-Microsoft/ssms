# Inventory V1

## Purpose

A stock register for consumables a society buys and uses up — bulbs, phenyl, garbage bags,
brooms. It answers four questions:

- What did the society buy?
- How much is left?
- What was used, when, and by whom?
- What still needs restocking?

It is deliberately **not** an asset register. There is no serial-number tracking, warranty, AMC,
depreciation, purchase orders, batches or multiple warehouses. It should feel like an Excel stock
register that happens to have logins, per-society isolation, an audit trail and a link to Expenses.

## Concepts

### Item master

One row per thing the society stocks. Items are **retired, never deleted** (`IsActive = false`), so
their history stays readable. Stock cannot be edited on the item — it only ever moves through a
recorded movement.

| Field               | Notes                                                                                                                    |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Name`              | Unique among _active_ items (filtered unique index, so a retired name can be reused)                                     |
| `Category`          | Free text, like `Expense.Category`. The UI suggests Electrical, Cleaning, Plumbing, Furniture, Security, Garden, General |
| `Unit`              | Nos, Pieces, Box, Pack, Bottle, Kg, Litre, Meter                                                                         |
| `MinimumStockLevel` | Drives the low-stock warning                                                                                             |
| `CurrentStock`      | Running balance (see _Concurrency_ below)                                                                                |
| `IsActive`          | Retire instead of deleting                                                                                               |

### Stock movements

Append-only. Nothing is ever edited or deleted — a mistake is corrected with a new Adjustment row.

`Quantity` is **signed**: `+20` received, `-2` consumed. That makes the reconciliation check a
plain `SUM` rather than a per-type `CASE`.

| Type         | Sign            | Books an expense?                               |
| ------------ | --------------- | ----------------------------------------------- |
| `Purchase`   | always positive | Optionally, if the admin ticks _Create Expense_ |
| `Usage`      | always negative | **Never**                                       |
| `Adjustment` | either          | **Never**                                       |

### Status

```
CurrentStock <= 0                    →  🔴 OutOfStock
CurrentStock <= MinimumStockLevel    →  🟠 LowStock
otherwise                            →  🟢 Good
```

Out-of-stock is evaluated first, so an empty shelf is never reported as merely low. Stock exactly
_at_ the minimum counts as low — that is the point of a reorder level.

## Rules

- **Stock can never go negative.** Stock Out beyond the balance returns
  `Insufficient stock. Available quantity: N.`
- **Usage never creates an expense.** The money was spent when the stock was bought; consuming it
  later is not a second cost.
- **Purchase amount is optional** unless _Create Expense_ is ticked, in which case it must be
  greater than zero. Societies that only want to track quantities never have to enter money.
- **Every Stock Out and Adjustment needs a reason.** Enforced in the controller, not only by the
  DTO attribute: on positional records `[Required]` binds to the constructor parameter, so the
  guard has to be real code.
- **Adjustments are Admin-only.** They move stock with no money and no document behind them, so
  they sit outside the `Edit` grant that Stock In/Out use.

## Concurrency

`CurrentStock` is a maintained running balance, **not** a derived value recomputed on read. It is
always written in the same `SaveChangesAsync()` as the movement that changed it, and the item
carries a `RowVersion` concurrency token.

If two admins each try to consume stock at the same moment, both may pass the availability check,
but only one `UPDATE` will match the row version. The loser receives
`409 Conflict — Someone else changed this item's stock at the same time. Reload and try again.`
rather than driving the balance negative.

`SUM(InventoryMovements.Quantity)` for an item must always equal `InventoryItems.CurrentStock`.
That is the invariant to check if the two ever look out of step.

## Expense integration

Ticking _Create Expense_ on Stock In builds an `Expense` and attaches it to the movement through
the navigation property, so EF stamps `InventoryMovement.ExpenseId` when both rows commit
together. One `SaveChangesAsync()` means both rows land or neither does — no explicit transaction
is used, matching the rest of the codebase (`UtilityBill.mark-paid` does the same).

The generated expense uses:

| Expense field                  | Source                                       |
| ------------------------------ | -------------------------------------------- |
| `ExpenseDate`, `Month`, `Year` | Purchase date                                |
| `Category`                     | Overridable; defaults to the item's category |
| `Description`                  | `"<Item> — <qty> <unit>"`                    |
| `Amount`                       | Purchase amount                              |
| `Vendor`, `PaymentMode`        | From the form                                |
| `Remarks`                      | `"Inventory purchase"` plus any note         |

> **Operator warning — double counting.** If a society already books "cleaning materials" on the
> Expenses tab _and_ starts using Stock In with _Create Expense_, the month is counted twice. Pick
> one route per category. The UI shows this warning when the box is ticked.

Because `Expense` is soft-deleted, deleting a linked expense leaves `ExpenseId` pointing at a
filtered-out row. The item history shows the id but the expense will not appear in expense lists.

## Access

Authorization reuses the existing per-user module permission system. `Inventory` was added to
`PermissionModules.All`; `PermissionHelper.Parse` defaults every module to `View`, so existing
users get read-only visibility of stock — which is the intended transparency.

| Capability                             | Required          |
| -------------------------------------- | ----------------- |
| View items, history, dashboard         | `Inventory: View` |
| Create/edit items, Stock In, Stock Out | `Inventory: Edit` |
| Adjust stock                           | `Admin` role      |
| Member read-only list                  | `Inventory: View` |

Caretakers are excluded outright by `CanView`/`CanEdit`, as with every other module. A future
watchman stock-out role would need a deliberate exception there — the seam exists, the feature
does not.

Members see `GET /api/me/inventory`, which projects **only** name, category, unit, stock and
status. Vendor, purchase amounts and the expense link are never sent to residents.

## Multi-tenancy

SMMS gives every society its **own database** (`SmmsDb_Aadya`, `SmmsDb_Demo`, …). There is no
`SocietyId`/`TenantId` column on any entity, and none was added here. `TenantResolutionMiddleware`
resolves the society from the host and `SmmsDbContext` refuses to be constructed without it, so a
society boundary cannot be crossed by changing an id in a request — there is no id to change.

## Database

Migration: **`AddInventoryV1`** (`20260908043035_AddInventoryV1`). Additive only; no existing
table is modified.

### `InventoryItems`

`Id`, `Name(100)`, `Category(60)`, `Unit(20)`, `MinimumStockLevel decimal(12,2)`,
`CurrentStock decimal(12,2)`, `RowVersion rowversion`, `IsActive bit`, plus audit columns.

- `IX_InventoryItems_Name` — unique, filtered `[IsActive] = 1`

### `InventoryMovements`

`Id`, `InventoryItemId`, `MovementType nvarchar(20)`, `Quantity decimal(12,2)`, `Reason(300)`,
`MovementDate`, `ExpenseId` (nullable), plus audit columns.

- `IX_InventoryMovements_InventoryItemId_MovementDate`
- `IX_InventoryMovements_ExpenseId`
- Both foreign keys use `ON DELETE RESTRICT`, so neither a retired item nor a deleted expense can
  orphan the stock register.

## API

| Method | Route                                   | Permission               |
| ------ | --------------------------------------- | ------------------------ |
| `GET`  | `/api/inventory/items?includeInactive=` | View                     |
| `GET`  | `/api/inventory/items/{id}`             | View                     |
| `GET`  | `/api/inventory/items/{id}/history`     | View                     |
| `GET`  | `/api/inventory/categories`             | View                     |
| `GET`  | `/api/inventory/dashboard`              | View                     |
| `POST` | `/api/inventory/items`                  | Edit                     |
| `PUT`  | `/api/inventory/items/{id}`             | Edit                     |
| `POST` | `/api/inventory/stock-in`               | Edit                     |
| `POST` | `/api/inventory/stock-out`              | Edit                     |
| `POST` | `/api/inventory/adjust`                 | **Admin**                |
| `GET`  | `/api/me/inventory`                     | View (member projection) |

There is no delete endpoint. Items are retired via `PUT` with `isActive: false`; movements are
never removed.

## UI

| Screen                                                        | Where                         |
| ------------------------------------------------------------- | ----------------------------- |
| Dashboard tiles, low-stock table, item list, recent movements | `UI/partials/inventory.html`  |
| Member read-only list                                         | `UI/partials/minventory.html` |
| Add/Edit item                                                 | `#modal-invitem`              |
| Stock In / Stock Out / Adjust (one form, mode-driven)         | `#modal-invmove`              |
| Item detail + full history                                    | `#modal-invdetail`            |

Navigation: **Inventory** for admins (with a badge counting items needing restock), **Inventory**
(read-only) for residents, and **Manage Inventory** for a resident explicitly granted
`Inventory: Edit`.

## Testing

`api/SMMS.Api.Tests/InventoryControllerTests.cs` — 25 tests using SQLite in-memory, one database
per test standing in for one society.

Covered: item creation, duplicate rejection, stock in, stock in with expense, expense amount
validation, optional purchase amount, stock out, usage never booking a second expense, negative
stock prevention (both from a partial balance and from zero), adjustment, negative adjustment
prevention, zero adjustment, mandatory reasons, all six status boundaries, member read-only,
admin full access, editor-can-move-but-not-adjust, cross-society isolation, and the full
acceptance scenario from the specification.

### Manual test

Use the **demo** society on **dev** — never a real society.

1. Inventory → **＋ Add Item**: `LED Bulb`, Electrical, Nos, minimum `5`. Stock shows `0` 🔴.
2. **Stock In** 20, amount `4000`, tick _Create Expense_. Stock `20` 🟢; a ₹4,000 expense appears
   on the Expenses tab.
3. **Stock Out** 2, reason `Block A corridor`. Stock `18`. No new expense.
4. **Stock Out** 14. Stock `4` 🟠, dashboard low-stock count rises.
5. **Stock Out** 4. Stock `0` 🔴.
6. **Stock Out** 1 → rejected: `Insufficient stock. Available quantity: 0.`
7. **Adjust** `+10`, reason `Opening stock correction`. Stock `10`.
8. Open the item — history shows 5 rows with who did what and the expense id on the purchase.
9. Log in as a resident → **Inventory** shows name/category/stock/status only, no buttons, no
   amounts.

## Limitations

- Stock is a single pool per society; there is no per-block or per-store location.
- No notifications. Low stock is a dashboard warning and a nav badge, nothing more.
- No stock valuation. Purchase amounts go to the expense, not onto the item, so there is no
  "value of stock on hand".
- Retiring an item hides it from the default list but its movements remain in the database.
- A resident granted `Inventory: Edit` uses the same admin screen; there is no reduced editor view.

## Possible V2

- Watchman role able to record usage without seeing money.
- Export to Excel, reusing the existing export helper.
- Per-item consumption trend, and reorder suggestions from usage rate.
- Attach the vendor bill to the purchase movement, reusing `UploadRules`.
- Link stock consumption to a complaint or maintenance job.
