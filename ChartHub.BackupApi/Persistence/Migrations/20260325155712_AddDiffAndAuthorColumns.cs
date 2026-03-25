using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChartHub.BackupApi.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDiffAndAuthorColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AuthorId",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "DiffBand",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DiffBass",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DiffDrums",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DiffGuitar",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DiffKeys",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DiffVocals",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GameFormat",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "GroupId",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_AuthorId",
            table: "SongSnapshots",
            column: "AuthorId");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_DiffDrums",
            table: "SongSnapshots",
            column: "DiffDrums");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_DiffGuitar",
            table: "SongSnapshots",
            column: "DiffGuitar");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_GameFormat",
            table: "SongSnapshots",
            column: "GameFormat");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_Genre",
            table: "SongSnapshots",
            column: "Genre");

        migrationBuilder.CreateIndex(
            name: "IX_SongSnapshots_GroupId",
            table: "SongSnapshots",
            column: "GroupId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_AuthorId",
            table: "SongSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_DiffDrums",
            table: "SongSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_DiffGuitar",
            table: "SongSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_GameFormat",
            table: "SongSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_Genre",
            table: "SongSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_SongSnapshots_GroupId",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "AuthorId",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "DiffBand",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "DiffBass",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "DiffDrums",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "DiffGuitar",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "DiffKeys",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "DiffVocals",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "GameFormat",
            table: "SongSnapshots");

        migrationBuilder.DropColumn(
            name: "GroupId",
            table: "SongSnapshots");
    }
}
