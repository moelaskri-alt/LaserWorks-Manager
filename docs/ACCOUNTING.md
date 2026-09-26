# Accounting

LaserWorks Manager keeps a real double-entry general ledger. Every operational transaction that
affects money, stock or job cost posts a balanced journal entry automatically, tagged with its source
document and, where relevant, the job, customer, supplier, machine and cost centre.

## Principles

- **Posted entries are permanent.** The database refuses any change to a posted entry's lines and any
  deletion. Mistakes are corrected with a reversing entry (Accounting → Journal entries → Reverse),
  or, for entries created by documents, by the matching document action (sales return, purchase return,
  reopening an operation, cancelling an expense).
- **Every entry balances.** Unbalanced entries are rejected at posting and again when saved.
- **Closed periods reject postings.** Periods are monthly inside fiscal years; closing and reopening
  are audited.
- **Estimated cost is never mixed with actual cost.** Estimates live only on estimates and quotations;
  the ledger and job cost sheets contain actual cost only.
- **History is not re-priced.** Stock issued earlier keeps the cost it was issued at; returns come
  back at their original cost.

## Chart of accounts (system accounts)

Created at setup; you can add your own accounts under the headers. System accounts cannot be deleted
and their type is fixed because the posting rules use them.

| Code | Account | Code | Account |
|---|---|---|---|
| 1010 | Cash on hand | 2100 | Accounts payable |
| 1020 | Bank | 2150 | Goods received not invoiced (GRNI) |
| 1100 | Accounts receivable | 2200 | Output tax (VAT) |
| 1200 | Inventory – raw materials | 2300 | Accrued wages |
| 1210 | Inventory – remnants | 3100 | Owner's capital |
| 1220 | Inventory – finished goods | 3200 | Retained earnings |
| 1230 | Inventory – consumables | 3900 | Opening balance equity |
| 1240 | Inventory – purchased components | | |
| 1250 | Inventory – packaging | | |
| 1300 | Work in progress (WIP) | 4100 | Sales revenue |
| 1400 | Input tax (VAT) | 4200 | Sales returns |
| 1500 / 1510 | Machinery / accumulated depreciation | 4900 | Other income |
| 5100 | Cost of goods sold (COGS) | 5200 / 5210 | Inventory loss / gain |
| 6100–6900 | Operating expenses (salaries, electricity, maintenance, rent, transport, packaging, marketing, depreciation, other) | 6950 / 6960 / 6970 | Labor / machine / overhead **applied** (contra-expense) |

## Posting rules

| Transaction | Debit | Credit |
|---|---|---|
| Opening stock | Inventory (by material kind) | Opening balance equity |
| Customer / supplier opening balance | AR / Opening equity | Opening equity / AP |
| Goods receipt | Inventory account of the item kind (raw materials, purchased components, consumables, packaging, finished goods) | GRNI |
| Supplier invoice (at receipt cost) | GRNI + Input tax | AP |
| Supplier payment | AP | Cash / Bank |
| Purchase return | AP + … (or GRNI if not yet invoiced) | Inventory at the original receipt cost, Input tax |
| Stock issue to a job component line | WIP (job) | Inventory account of the item kind, at moving average cost |
| Direct purchase / external service / manual cost on a job component line | WIP (job) + Input tax | AP (supplier) or Cash / Bank |
| Reversal of such a direct cost | the reversing entry of the original | |
| Material return from job | Inventory | WIP (job), at the cost it was issued at |
| Operation completed | WIP (job) | Machine applied (machine + maintenance), Labor applied |
| Rework | WIP (job) | Machine applied / Labor applied |
| Overhead (at completion, quality check, invoice, close; only the difference is posted) | WIP (job) | Overhead applied |
| Remnant saved from a job | Remnant inventory | WIP (job), area-proportional value |
| Remnant used on a job | WIP (job) | Remnant inventory |
| Stock adjustment / scrap from stock | Inventory or Inventory loss | Inventory gain or Inventory |
| Expense (general) | Expense account + Input tax | Cash / Bank / AP |
| Expense charged to a job | WIP (job) + Input tax | Cash / Bank / AP |
| Sales invoice | AR (customer) | Sales, Output tax |
| — job line | COGS (job) | WIP (job): all job cost accumulated so far |
| — stock item line | COGS | Inventory at moving average cost |
| Customer payment | Cash / Bank | AR (customer) |
| Sales return | Sales returns + Output tax | AR (or Cash / Bank if refunded) |
| — stock item restocked | Inventory at the **original** unit cost of the sale | COGS |
| Close job (cost posted after invoicing) | COGS (job) | WIP (job) |

Normal scrap does not post: it re-labels part of the job's material cost as *Scrap* on the job cost
sheet so it is visible without changing the job total.

The *applied* accounts are credit-balance contra-expenses: real salaries, electricity and maintenance
bills are posted as expenses when paid, and the applied accounts show how much of them was absorbed
into jobs. The difference between the two is the under- or over-absorbed cost of the period.

## Reports and reconciliation

Trial balance, general ledger, income statement, balance sheet, cash-flow summary, AR/AP aging and
customer statements are built from posted lines. **Accounting → Reconciliation** checks that:

- total debits equal total credits, and every posted entry balances
- each inventory account equals the stock value of its materials, remnant inventory equals available remnants
- WIP equals the cost of open jobs, and each job's cost equals its cost entries
- stock quantities equal warehouse balances and the stock ledger
- AR equals open customer invoices and every AR/AP line names a customer/supplier

The same checks run in the automated tests after every scenario.
