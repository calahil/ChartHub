using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChartHub.BackupApi.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SongSnapshots",
            columns: table => new
            {
                SongId = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                RecordId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Album = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Genre = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Year = table.Column<int>(type: "INTEGER", nullable: true),
                RecordUpdatedUnix = table.Column<long>(type: "INTEGER", nullable: true),
                FileId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                SongJson = table.Column<string>(type: "TEXT", nullable: false),
                DataJson = table.Column<string>(type: "TEXT", nullable: false),
                FileJson = table.Column<string>(type: "TEXT", nullable: false),
                LastSyncedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SongSnapshots", x => x.SongId);
            });

        migrationBuilder.CreateTable(
            name: "SyncStates",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SyncStates", x => x.Key);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_Artist",
            table: "SongSnapshots",
            column: "Artist");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_FileId",
            table: "SongSnapshots",
            column: "FileId");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_LastSyncedUtc",
            table: "SongSnapshots",
            column: "LastSyncedUtc");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_RecordId",
            table: "SongSnapshots",
            column: "RecordId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_RecordUpdatedUnix",
            table: "SongSnapshots",
            column: "RecordUpdatedUnix");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_Title",
            table: "SongSnapshots",
            column: "Title");

        migrationBuilder.CreateIndex(
            name: "IX_SyncStates_UpdatedUtc",
            table: "SyncStates",
            column: "UpdatedUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SongSnapshots");

        migrationBuilder.DropTable(
            name: "SyncStates");
    }
}
