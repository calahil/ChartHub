using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChartHub.BackupApi.Persistence.Migrations;

/// <inheritdoc />
public partial class AddReconciliationTracking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "SongSnapshots",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "LastReconciledRunId",
            table: "SongSnapshots",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_IsDeleted",
            table: "SongSnapshots",
            column: "IsDeleted");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_IsDeleted_LastReconciledRunId",
            table: "SongSnapshots",
            columns: new[] { "IsDeleted", "LastReconciledRunId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_IsDeleted",
            table: "SongSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_IsDeleted_LastReconciledRunId",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "LastReconciledRunId",
            table: "SongSnapshots");
    }
}
