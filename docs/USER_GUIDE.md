# User guide

## 1. Installation

**Installer.** Run `LaserWorksManagerSetup.exe` (Windows 10 or 11, 64-bit) and follow the steps. It adds
Start-menu and desktop shortcuts and an uninstaller. Your company data is stored in
`%LOCALAPPDATA%\LaserWorksManager` and is kept when you uninstall or upgrade.

**Portable.** Unzip `LaserWorksManager-…-portable.zip` into any folder (or a USB stick) and run
`LaserWorksManager.exe`. Because of the `portable.flag` file, data is kept in the `data` folder next to it.

Windows SmartScreen may warn about an unsigned publisher on first start; choose *More info → Run anyway*.

## 2. First setup

The first start opens the setup wizard:

1. **Company** — name, address, phone, tax number. Choose Arabic or English (switch any time from the top bar).
2. **Currency, tax and costing** — currency code and symbol, VAT %, decimal places, target and minimum margin,
   overhead rate, default labor rate, electricity price.
3. **Fiscal year** — the first month of your financial year.
4. **Warehouses** — at least one.
5. **Machines** — purchase cost, life, residual value, annual hours, electrical load, maintenance. The hourly cost is calculated for you.
6. **Materials** — name, sheet size and thickness, unit, cost and opening quantity.
7. **Users** — the administrator account and optional users with roles.
8. **Opening balances** — cash and bank.
9. **Finish** — or tick *Load demo data* to explore with a sample company.

Everything can be changed later under Settings, Machines, Materials and Users.

## 3. Materials and stock

**Items & Materials → New material**: code (auto-numbered), name, kind (raw material, purchased component,
consumable, packaging, finished product or service — services are never stocked),
type, thickness, sheet length and width (for sheet materials use the *Sheet* unit), purchase and sales price,
reorder level. Stock quantity and average cost cannot be typed; they change only through movements:

- **Opening stock**, **Adjustment** (count differences), **Transfer** between warehouses, **Scrap** from stock
- **Issue to job** / **Return from job** (normally from the job screen)
- **Purchases → Goods receipt** from an approved purchase order, then **Supplier invoice** and **Supplier payment**

Inventory → *Stock movements* is the stock ledger; *Remnants* lists reusable offcuts; *Utilization calculator*
estimates sheets for a set of pieces; *Laser parameters* stores reference power/speed settings per material.

## 4. Machines and employees

**Machines → New machine**: enter the purchase and running costs; the right-hand panel shows the hourly rate
build-up (depreciation, electricity, maintenance, operator, overhead). Tick *Override hourly rate* to use your
own rate. **Employees & Labor**: name, role and hourly cost; used for labor on operations and estimates.

## 5. Customer request and design

**Customer requests → New request**: customer, description, dimensions, quantity and required date, and on the
**Requested items** tab every material, component, accessory, packaging or service the customer asked for —
as many lines as needed, each with its quantity per finished unit (pick an item, or type a free-text item and
choose its type). **Add item**, **Duplicate** and **Remove** manage the lines. Click **Save and continue**
(the dialog stays open), then:

- **Attachments** tab — add, open or remove reference images, logos, DXF/SVG/PDF files (name, type, size, date
  and user are shown; stored inside the data folder)
- **Design revision** tab — **New revision** (design size, main sheet material, cutting length, engraving area,
  estimated machine minutes, design files); **Open** shows a revision
- **Approve as final** on the chosen revision (other revisions become *Superseded*)
- **Create estimate** — opens a cost estimate prefilled from the request and approved design

## 6. Cost estimate

The estimate editor recalculates as you type:

- **Components & materials**: a grid with one line per component — type, source, item, unit, quantity per unit,
  total quantity, unit cost and cost. Add lines **From inventory**, **Purchased for job** (bought directly for this
  job), **External service**, **Use remnant** (pick the offcut) or **Other cost**; **Duplicate** / **Remove** the
  selected line. The panel under the grid edits the selected line; **New item** creates a missing item in the item
  master on the spot. For sheet materials add the pieces per unit (length, width, quantity) and check sheets
  required, utilisation and waste; override sheets or use *Find remnants*. There is no limit on the number of lines.
- **Machine time**: machine, operation and minutes per unit. **Labor**: employee, operation, minutes per unit.
- **Other costs**: design and setup hours, finishing, packaging and consumables per unit, overhead, scrap and
  rework allowance.
- **Cost breakdown**: tick *Manual* on any component to type its amount.
- **Pricing**: target and minimum margin, suggested price, selling price, profit, margin and markup; a warning
  appears below the minimum margin. **What-if** tries other quantities, costs, margins or prices.

**Save**, then **Create quotation** (this finalises the estimate). *Duplicate* makes a new draft from a final estimate.
**Save as template** keeps the estimate's bill of materials and routing as a product template;
**Estimates → New from template** starts a new estimate from a template (templates can be deleted there too).

## 7. Quotation

The quotation dialog shows the price, discount, VAT, validity, delivery days and payment terms.
**PDF** / **Print** produce the customer quotation. **Mark as sent**, then **Approve** or **Reject**.
**New version** keeps the history when the customer negotiates. On an approved quotation choose the due date,
priority, machine and operator and click **Create job**, then **Open job**.

A job can also be created directly (Jobs → *New job (without quotation)*) for repeat work.

## 8. Job and production

The job screen shows selling price, estimated and actual cost, variance, profit and margin, with tabs for
cost, components, operations, materials, scrap and rework, quality and details.

1. **Components** tab: the job's component lines, taken from the estimate (planned quantity, used quantity,
   remaining, estimated and actual cost, variance). Select a line and:
   - **Issue material** / **Return material** for stock lines (the quantity defaults to what is still needed),
   - **Use remnant** for raw-material lines,
   - **Record direct cost** for lines purchased for the job, external services and other costs — the supplier
     bill (on credit) or cash/bank payment is posted straight to the job, never through stock
     (a wrong one is reversed from the cost tab with **Reverse direct cost**).
   **From inventory / Purchased for job / External service / Use remnant / Other cost** add lines during
   production; **Edit**, **Duplicate** and **Delete** manage them (a line that already carries cost keeps its item,
   source and type). **Save job as template** keeps the job's actual bill of materials for repeat products.
   The **Issue material** button at the top and the Inventory page still work: the movement is attached to the
   job's line for that item, or to a new *unplanned* line.
2. **Operations** are created from the estimate (design, preparation, cutting/engraving, finishing…).
   **Start** and **Complete** each one with actual labor hours, machine hours, quantity, employee and machine;
   the cost is posted immediately. Add or edit operations as needed; the Production → *Operations board*
   shows work for all jobs.
3. **Complete production** when all operations are done.
4. **Quality check**: produced, accepted, rejected and rework quantities.
5. **Deliver** with the delivery date and note.
6. **Create invoice** → **Post invoice** → **Record payment**.
7. **Close job** after invoicing: any cost posted later is moved to cost of sales.

## 9. Scrap and rework

On the job screen: **Record scrap** — type (normal, abnormal, damage, material waste), material, quantity and
reason. The cost is taken from the job's material cost (or typed) and shown as *Scrap* on the cost sheet.
**Record rework** — machine, employee, hours and reason; rework hours are costed at the machine and employee
rates and added to the job. Production → *Scrap & rework* lists all records.

## 10. Remnants

On a job: **Create remnant** with its length and width. Its value (area share of the sheet, or the value you
enter) leaves the job cost and goes to remnant inventory. Later, from another job: **Use remnant** moves the
value into that job. Inventory → Remnants lets you adjust or scrap old remnants.

## 11. Understanding job costing

Actual cost is built only from real transactions: stock issued to component lines, remnants used, direct
purchases and external services recorded on lines, machine and labor time on operations, rework, expenses
charged to the job and overhead. The **Cost & variance** tab compares every component line (and every cost
component) with the estimate and names the main reason for the difference. **Cost sheet (PDF)** and **Excel** export the full
job cost sheet. See [COSTING.md](COSTING.md).

## 12. Understanding profitability

**Job profitability** shows revenue, estimated and actual cost, gross profit, margin and variances for every
job, with flags (loss, low margin, high material or machine variance, high scrap). Other tabs group results by
customer, machine, material and month. Click a row to open the job. The **Reports** page adds 40 reports in
seven groups, each exportable to PDF, Excel and CSV or printable.

## 13. Sales, purchases, expenses and accounting

- **Sales & invoicing**: invoices for jobs, stock items or services; payments; sales returns (stock returns
  at its original cost).
- **Purchases**: suppliers, purchase orders, goods receipts, supplier invoices, payments and returns.
- **Expenses**: rent, electricity, salaries…; charge an expense to a job to add it to the job cost.
- **Accounting**: chart of accounts, journal entries (manual entries and reversals), account ledger, trial
  balance, fiscal periods (close/reopen) and reconciliation. See [ACCOUNTING.md](ACCOUNTING.md).

## 14. Backup and restore

**Backup & Restore → Back up now** creates a `.lwbak` file (database + attachments + checksum) in the
`backups` folder; **Back up to…** saves it anywhere (USB drive, network folder). **Validate file…** checks a
backup. **Restore selected** / **Restore from file…** first validates the file and always creates a safety
backup of the current data before replacing it; you then sign in again. **Run integrity check** verifies the
database and that the books balance. Back up at least daily and keep copies off the computer.

## 15. Users and permissions

Roles: Administrator, Manager, Accountant, Sales, Production, Storekeeper. **Users & Permissions** lets the
administrator create users, reset passwords, unlock accounts and edit what each role may view, create, edit,
delete, post, approve, print and export per module. Five wrong passwords lock an account for 15 minutes.
The *Audit trail* tab lists who changed what and when.
