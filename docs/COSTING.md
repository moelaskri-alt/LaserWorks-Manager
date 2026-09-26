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

Eleven components, each automatic or manual (a manual amount replaces the calculation):

| Component | Automatic calculation |
|---|---|
| Material | Σ material lines (sheets × cost, or quantity × average cost) |
| Machine time | Σ machine hours × machine rate (excluding maintenance) |
| Maintenance allocation | Σ machine hours × maintenance rate |
| Labor | Σ minutes per unit × quantity ÷ 60 × hourly rate |
| Design | design hours × design rate |
| Setup | setup hours × setup rate |
| Finishing, Packaging, Consumables | per-unit amount × quantity |
| Scrap allowance | scrap % × material |
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
| Material issued (net of returns, remnants saved or used) | Material |
| Operation completed: machine hours × rate | Machine time + Maintenance allocation |
| Operation completed: labor hours × employee rate | Labor (Design / Setup for those operations) |
| Normal scrap | re-labels Material → Scrap |
| Rework hours | Rework |
| Expenses charged to the job | component of the expense category |
| Overhead | Overhead (topped up at completion, QC, invoice and close) |

## Estimated vs actual and profitability

The job's **Cost & variance** tab compares each component: variance = actual − estimated
(positive = over budget) and names the main variance driver. The job cost sheet (PDF / Excel) shows the
same with every cost entry, material movement, operation, scrap and quality record.

Profit uses the invoiced revenue (or the agreed selling price before invoicing):
gross profit = revenue − actual cost, margin = gross profit ÷ revenue. **Job profitability** lists every
job with flags for losses, low margin, high material or machine variance and high scrap (thresholds in
Settings → Costing), and groups results by customer, machine, material and month.
