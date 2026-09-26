# Costing

## Machine hourly cost

For each machine (Machines → Edit):

```
depreciation / h = (purchase cost − residual value) ÷ (useful life years × annual working hours)
electricity  / h = electrical load kW × electricity price per kWh
maintenance  / h = annual maintenance ÷ annual working hours
operator     / h = operator cost per hour (leave 0 if operators are recorded as labor on operations)
overhead     / h = other annual overhead ÷ annual working hours
hourly rate      = sum of the above, or a manual override rate
```

Example: 60,000 purchase, 5,000 residual, 5 years × 2,000 h, 1.8 kW at 0.18, maintenance 3,000/year,
other overhead 2,000/year → 5.50 + 0.324 + 1.50 + 1.00 = **8.324 per hour**.
Machine time on jobs posts the rate excluding maintenance as *Machine time* and the maintenance part
as *Maintenance allocation*, so both can be analysed.

## Component lines (multi-material jobs)

A job is never limited to one material. Requests, estimates and jobs carry any number of **component lines**
(0…N). Each line has a **type** and a **source**:

| Type | Examples | Cost component |
|---|---|---|
| Raw material | MDF, plywood, acrylic, leather sheets | Material |
| Purchased component | LED strip, adapter, switch, screws, handles | Purchased components |
| Consumable | glue, tape, paint | Consumables |
| Packaging | gift box, carton, bubble wrap | Packaging |
| External service | UV printing, painting, installation by a subcontractor | External services |
| Other direct cost | transport, anything else charged to the job | Other direct costs |

| Source | How the line gets its actual cost |
|---|---|
| Inventory | stock issued to the line (moving average cost); returns reduce it |
| Remnant | the value of the remnant used on the line |
| Direct purchase | the supplier bill recorded on the line — the item never passes through stock |
| External service | the subcontractor bill recorded on the line |
| Manual cost | a cash/bank/credit amount recorded on the line |

**No double charging.** A stock line can only be costed by stock issues or remnants; a direct line can only
be costed by a direct cost recorded on it (the app refuses the other way round). A purchased component can
therefore take either route — bought into stock and issued (Purchase → Receipt → Issue → Job cost) or bought
for the job directly (Purchase → Direct job cost) — never both.

The item master distinguishes raw materials, purchased components, consumables, packaging, finished products
and services. Services are never held in stock; remnants can only be made of raw materials.

**Where lines come from.** The request lists the requested items (quantity per finished unit, free text allowed).
A new estimate starts with one line per requested item; on the estimate you add, duplicate, remove or change lines
(from inventory, purchased for the job, external service, remnant, other cost, or a **new item** created on the spot).
The job receives one line per estimate line with the estimated quantity and cost as a fixed snapshot; lines can be
added during production. An issue without a line (e.g. from the Inventory page) lands on a new *unplanned* line,
so every stock movement of a job belongs to a line.

**Product templates.** An estimate or a finished job can be saved as a product template (its bill of materials,
machine and labor structure). *Estimates → New from template* starts a new estimate from it: stock lines are
re-priced at today's average cost, direct purchases and services keep the template's price.
Custom jobs never need a template.

## Material and sheet utilisation

Sheet materials (acrylic, MDF, plywood…) are estimated from the pieces per unit:

- **Area method**: sheets = ⌈ total piece area ÷ (sheet area × nesting efficiency) ⌉
- **Grid method**: each piece laid out alone in a simple grid in its best orientation — an upper bound
- The sheet count can be overridden after checking the layout manually; utilisation above 100 % is rejected
- Material cost = sheets × cost per sheet, or only the consumed fraction when "charge full sheets" is off
  (the rest is expected to become a remnant)

This is a practical calculator, not a nesting engine. **Inventory → Utilization calculator** runs the
same calculation without an estimate.

## Remnants

An offcut worth keeping is recorded as a remnant (length × width, material, warehouse). Its value is the
area-proportional share of the sheet cost (or a value you enter) and moves from the job (or stock) into
remnant inventory. When a later job uses it, the value moves into that job. Estimates can search for
available remnants that fit.

## Estimate

Fifteen components, each automatic or manual (a manual amount replaces the calculation):

| Component | Automatic calculation |
|---|---|
| Material | Σ raw-material lines (sheets × cost per sheet, remnant value, or quantity × unit cost) |
| Purchased components | Σ purchased-component lines |
| Consumables, Packaging | Σ lines of that type + per-unit amount × quantity |
| External services | Σ external-service lines |
| Machine time | Σ machine hours × machine rate (excluding maintenance) |
| Maintenance allocation | Σ machine hours × maintenance rate |
| Labor | Σ minutes per unit × quantity ÷ 60 × hourly rate |
| Design | design hours × design rate |
| Setup | setup hours × setup rate |
| Finishing | per-unit amount × quantity |
| Other direct costs | Σ other-direct-cost lines |
| Scrap allowance | scrap % × material |
| Rework allowance | rework % × (machine + maintenance + labor) |
| Overhead | overhead % × direct cost, or rate × machine hours |

Total cost = direct cost + overhead. Suggested price = cost ÷ (1 − target margin);
minimum price uses the minimum margin, and the editor warns when the price is below it.
A new estimate is priced at the suggested price until you type a price.
**What-if** tries another quantity, material cost, machine time, margin or price and shows the resulting
cost and profit without changing the estimate. A final estimate is locked; duplicate it to revise.

Margin and markup are different: margin = profit ÷ price, markup = profit ÷ cost
(30 % margin on a cost of 70 is a price of 100; that is a 42.86 % markup).

## Actual job cost

A job's actual cost is the sum of its cost entries, each from a real transaction:

| Source | Component |
|---|---|
| Stock issued to a line (net of returns), remnants saved or used | the line's type: Material, Purchased components, Consumables or Packaging |
| Direct purchase / external service / manual cost recorded on a line | Purchased components, External services, … (the line's type) |
| Operation completed: machine hours × rate | Machine time + Maintenance allocation |
| Operation completed: labor hours × employee rate | Labor (Design / Setup for those operations) |
| Normal scrap | re-labels part of the material line's cost as Scrap |
| Rework hours | Rework |
| Expenses charged to the job | component of the expense category |
| Overhead | Overhead (topped up at completion, QC, invoice and close) |

## Estimated vs actual and profitability

The job's **Cost & variance** tab compares every **component line** (planned and used quantity, estimated and
actual cost, variance) and, below, every cost component; variance = actual − estimated (positive = over budget)
and the main variance driver is named. Costs that are not on a line (machine, labor, overhead, scrap, rework, and
the estimate's per-unit allowances) appear as separate rows, so the line rows plus these rows add up exactly to the
estimated total and to the actual job cost. The **Components** tab manages the lines themselves. The *Job components*
and *Estimated vs actual* reports and the job cost sheet show the same by line. The job cost sheet (PDF / Excel) shows the
same with every cost entry, material movement, operation, scrap and quality record.

Profit uses the invoiced revenue (or the agreed selling price before invoicing):
gross profit = revenue − actual cost, margin = gross profit ÷ revenue. **Job profitability** lists every
job with flags for losses, low margin, high material or machine variance and high scrap (thresholds in
Settings → Costing), and groups results by customer, machine, material and month.
