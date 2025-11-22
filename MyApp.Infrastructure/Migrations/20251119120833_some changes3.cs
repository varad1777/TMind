using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class somechanges3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "DeviceSlaveSets");

            migrationBuilder.DropColumn(
                name: "State",
                table: "DeviceSlaveSets");

            migrationBuilder.AddColumn<string>(
                name: "ByteOrder",
                table: "Registers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WordSwap",
                table: "Registers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ByteOrder",
                table: "Registers");

            migrationBuilder.DropColumn(
                name: "WordSwap",
                table: "Registers");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "DeviceSlaveSets",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "DeviceSlaveSets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
