using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LaserWorks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiComponentJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "JobComponentId",
                table: "JobCostEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "JobComponentId",
                table: "InventoryTransactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "MaterialId",
                table: "EstimateMaterialLines",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "EstimateMaterialLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "EstimateMaterialLines",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LineNo",
                table: "EstimateMaterialLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RemnantId",
                table: "EstimateMaterialLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "EstimateMaterialLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "EstimateMaterialLines",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReworkAllowancePercent",
                table: "CostEstimates",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "JobComponents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<long>(type: "INTEGER", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialId = table.Column<long>(type: "INTEGER", nullable: true),
                    RemnantId = table.Column<long>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    EstimatedUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    EstimateLineId = table.Column<long>(type: "INTEGER", nullable: true),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobComponents_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobComponents_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobComponents_Remnants_RemnantId",
                        column: x => x.RemnantId,
                        principalTable: "Remnants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobComponents_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Definition = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceEstimateId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialId = table.Column<long>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestItems_CustomerRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "CustomerRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestItems_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCostEntries_JobComponentId",
                table: "JobCostEntries",
                column: "JobComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_JobComponentId",
                table: "InventoryTransactions",
                column: "JobComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_EstimateMaterialLines_RemnantId",
                table: "EstimateMaterialLines",
                column: "RemnantId");

            migrationBuilder.CreateIndex(
                name: "IX_JobComponents_JobId_LineNo",
                table: "JobComponents",
                columns: new[] { "JobId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_JobComponents_MaterialId",
                table: "JobComponents",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_JobComponents_RemnantId",
                table: "JobComponents",
                column: "RemnantId");

            migrationBuilder.CreateIndex(
                name: "IX_JobComponents_SupplierId",
                table: "JobComponents",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTemplates_Code",
                table: "ProductTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_MaterialId",
                table: "RequestItems",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_RequestId",
                table: "RequestItems",
                column: "RequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_EstimateMaterialLines_Remnants_RemnantId",
                table: "EstimateMaterialLines",
                column: "RemnantId",
                principalTable: "Remnants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_JobComponents_JobComponentId",
                table: "InventoryTransactions",
                column: "JobComponentId",
                principalTable: "JobComponents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobCostEntries_JobComponents_JobComponentId",
                table: "JobCostEntries",
                column: "JobComponentId",
                principalTable: "JobComponents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---------------------------------------------------------------- data migration
            // Existing data moves into the multi-component structure. Posted amounts, stock movements and cost entries
            // are not changed; they are only linked to the component line they belong to.
            // MaterialKind → ComponentCategory: Raw 1→1, Consumable 2→3, FinishedGood 3→2 (purchased), Component 4→2, Packaging 5→4, Service 6→5.

            // 1. the single material of each request becomes its first requested item
            migrationBuilder.Sql(@"INSERT INTO RequestItems (RequestId, LineNo, Category, MaterialId, Description, Quantity, Unit, Notes)
                SELECT r.Id, 1, CASE m.Kind WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 4 THEN 2 WHEN 5 THEN 4 WHEN 6 THEN 5 ELSE 1 END, r.MaterialId, NULL, '1.0', u.Code, NULL
                FROM CustomerRequests r JOIN Materials m ON m.Id = r.MaterialId LEFT JOIN Units u ON u.Id = m.UnitId
                WHERE r.MaterialId IS NOT NULL;");

            // 2. estimate material lines become categorised component lines sourced from inventory
            migrationBuilder.Sql(@"UPDATE EstimateMaterialLines SET
                Category = COALESCE((SELECT CASE m.Kind WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 4 THEN 2 WHEN 5 THEN 4 WHEN 6 THEN 5 ELSE 1 END FROM Materials m WHERE m.Id = EstimateMaterialLines.MaterialId), 1),
                Unit = (SELECT u.Code FROM Materials m JOIN Units u ON u.Id = m.UnitId WHERE m.Id = EstimateMaterialLines.MaterialId),
                Source = 1, LineNo = Id
                WHERE Category = 0;");

            // 3. every job created from an estimate gets the estimate's lines as its planned components
            migrationBuilder.Sql(@"INSERT INTO JobComponents (JobId, LineNo, Category, Source, MaterialId, RemnantId, Description, Unit, PlannedQuantity, EstimatedUnitCost, EstimatedCost, EstimateLineId, CreatedAt, CreatedBy)
                SELECT j.Id, ROW_NUMBER() OVER (PARTITION BY j.Id ORDER BY l.LineNo, l.Id), l.Category, l.Source, l.MaterialId, NULL, l.Description, l.Unit,
                       l.TotalQuantity, l.UnitCost, l.Cost, l.Id, j.CreatedAt, 'migration'
                FROM Jobs j JOIN EstimateMaterialLines l ON l.EstimateId = j.EstimateId;");

            // 4. materials issued (or used as remnants / saved as remnants) without a planned line get an unplanned line
            migrationBuilder.Sql(@"INSERT INTO JobComponents (JobId, LineNo, Category, Source, MaterialId, Description, Unit, PlannedQuantity, EstimatedUnitCost, EstimatedCost, CreatedAt, CreatedBy)
                SELECT x.JobId, 100 + ROW_NUMBER() OVER (PARTITION BY x.JobId ORDER BY x.MaterialId), CASE m.Kind WHEN 2 THEN 3 WHEN 3 THEN 2 WHEN 4 THEN 2 WHEN 5 THEN 4 WHEN 6 THEN 5 ELSE 1 END, 1, x.MaterialId, NULL, u.Code, '0.0', '0.0', '0.0', j.CreatedAt, 'migration'
                FROM (SELECT DISTINCT t.JobId, t.MaterialId FROM InventoryTransactions t
                      WHERE t.JobId IS NOT NULL AND t.MaterialId IS NOT NULL AND t.Type IN (4, 5, 9, 10)
                      UNION SELECT DISTINCT e.JobId, e.MaterialId FROM JobCostEntries e WHERE e.MaterialId IS NOT NULL AND e.Component = 1) x
                JOIN Jobs j ON j.Id = x.JobId JOIN Materials m ON m.Id = x.MaterialId LEFT JOIN Units u ON u.Id = m.UnitId
                WHERE NOT EXISTS (SELECT 1 FROM JobComponents c WHERE c.JobId = x.JobId AND c.MaterialId = x.MaterialId);");

            // 5. link existing stock movements and material cost entries to their component line
            migrationBuilder.Sql(@"UPDATE InventoryTransactions SET JobComponentId =
                (SELECT c.Id FROM JobComponents c WHERE c.JobId = InventoryTransactions.JobId AND c.MaterialId = InventoryTransactions.MaterialId ORDER BY c.LineNo LIMIT 1)
                WHERE JobId IS NOT NULL AND MaterialId IS NOT NULL AND Type IN (4, 5, 9, 10);");
            migrationBuilder.Sql(@"UPDATE JobCostEntries SET JobComponentId =
                (SELECT c.Id FROM JobComponents c WHERE c.JobId = JobCostEntries.JobId AND c.MaterialId = JobCostEntries.MaterialId ORDER BY c.LineNo LIMIT 1)
                WHERE MaterialId IS NOT NULL AND Component = 1;");

            // the request's single-material columns are dropped only after their data was copied

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRequests_Materials_MaterialId",
                table: "CustomerRequests");

            migrationBuilder.DropIndex(
                name: "IX_CustomerRequests_MaterialId",
                table: "CustomerRequests");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                table: "CustomerRequests");

            migrationBuilder.DropColumn(
                name: "Thickness",
                table: "CustomerRequests");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EstimateMaterialLines_Remnants_RemnantId",
                table: "EstimateMaterialLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_JobComponents_JobComponentId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_JobCostEntries_JobComponents_JobComponentId",
                table: "JobCostEntries");

            migrationBuilder.DropTable(
                name: "JobComponents");

            migrationBuilder.DropTable(
                name: "ProductTemplates");

            migrationBuilder.DropTable(
                name: "RequestItems");

            migrationBuilder.DropIndex(
                name: "IX_JobCostEntries_JobComponentId",
                table: "JobCostEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_JobComponentId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_EstimateMaterialLines_RemnantId",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "JobComponentId",
                table: "JobCostEntries");

            migrationBuilder.DropColumn(
                name: "JobComponentId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "LineNo",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "RemnantId",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "EstimateMaterialLines");

            migrationBuilder.DropColumn(
                name: "ReworkAllowancePercent",
                table: "CostEstimates");

            migrationBuilder.AlterColumn<long>(
                name: "MaterialId",
                table: "EstimateMaterialLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaterialId",
                table: "CustomerRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Thickness",
                table: "CustomerRequests",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRequests_MaterialId",
                table: "CustomerRequests",
                column: "MaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRequests_Materials_MaterialId",
                table: "CustomerRequests",
                column: "MaterialId",
                principalTable: "Materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
