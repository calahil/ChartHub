using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChartHub.BackupApi.Persistence.Migrations;

/// <inheritdoc />
public partial class AlignPostgresModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Value",
            table: "SyncStates",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 4000);

        migrationBuilder.Sql(
            "ALTER TABLE \"SyncStates\" ALTER COLUMN \"UpdatedUtc\" TYPE timestamp with time zone USING \"UpdatedUtc\"::timestamp with time zone;");

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "SyncStates",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<int>(
            name: "Year",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Title",
            table: "SongSnapshots",
            type: "character varying(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "SongJson",
            table: "SongSnapshots",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<long>(
            name: "RecordUpdatedUnix",
            table: "SongSnapshots",
            type: "bigint",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "RecordId",
            table: "SongSnapshots",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 256);

        migrationBuilder.Sql(
            "ALTER TABLE \"SongSnapshots\" ALTER COLUMN \"LastSyncedUtc\" TYPE timestamp with time zone USING \"LastSyncedUtc\"::timestamp with time zone;");

        migrationBuilder.AlterColumn<string>(
            name: "GroupId",
            table: "SongSnapshots",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "Genre",
            table: "SongSnapshots",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "GameFormat",
            table: "SongSnapshots",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "FileJson",
            table: "SongSnapshots",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<string>(
            name: "FileId",
            table: "SongSnapshots",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "DownloadUrl",
            table: "SongSnapshots",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 2048);

        migrationBuilder.AlterColumn<int>(
            name: "DiffVocals",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffKeys",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffGuitar",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffDrums",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffBass",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffBand",
            table: "SongSnapshots",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "DataJson",
            table: "SongSnapshots",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<string>(
            name: "AuthorId",
            table: "SongSnapshots",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "Artist",
            table: "SongSnapshots",
            type: "character varying(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "Album",
            table: "SongSnapshots",
            type: "character varying(512)",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<long>(
            name: "SongId",
            table: "SongSnapshots",
            type: "bigint",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER")
            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Value",
            table: "SyncStates",
            type: "TEXT",
            maxLength: 4000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(4000)",
            oldMaxLength: 4000);

        migrationBuilder.Sql(
            "ALTER TABLE \"SyncStates\" ALTER COLUMN \"UpdatedUtc\" TYPE TEXT USING \"UpdatedUtc\"::text;");

        migrationBuilder.AlterColumn<string>(
            name: "Key",
            table: "SyncStates",
            type: "TEXT",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<int>(
            name: "Year",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Title",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(512)",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "SongJson",
            table: "SongSnapshots",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<int>(
            name: "RecordUpdatedUnix",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(long),
            oldType: "bigint",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "RecordId",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256);

        migrationBuilder.Sql(
            "ALTER TABLE \"SongSnapshots\" ALTER COLUMN \"LastSyncedUtc\" TYPE TEXT USING \"LastSyncedUtc\"::text;");

        migrationBuilder.AlterColumn<string>(
            name: "GroupId",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "Genre",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "GameFormat",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "FileJson",
            table: "SongSnapshots",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "FileId",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "DownloadUrl",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 2048,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(2048)",
            oldMaxLength: 2048);

        migrationBuilder.AlterColumn<int>(
            name: "DiffVocals",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffKeys",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffGuitar",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffDrums",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffBass",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "DiffBand",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "DataJson",
            table: "SongSnapshots",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "AuthorId",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128);

        migrationBuilder.AlterColumn<string>(
            name: "Artist",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(512)",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<string>(
            name: "Album",
            table: "SongSnapshots",
            type: "TEXT",
            maxLength: 512,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(512)",
            oldMaxLength: 512);

        migrationBuilder.AlterColumn<int>(
            name: "SongId",
            table: "SongSnapshots",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint")
            .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
    }
}
