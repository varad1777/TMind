using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class somechanges2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DevicePorts_DeviceId_PortIndex",
                table: "DevicePorts");

            migrationBuilder.DropColumn(
                name: "DataType",
                table: "DevicePorts");

            migrationBuilder.DropColumn(
                name: "RegisterAddress",
                table: "DevicePorts");

            migrationBuilder.DropColumn(
                name: "RegisterLength",
                table: "DevicePorts");

            migrationBuilder.DropColumn(
                name: "Scale",
                table: "DevicePorts");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "DevicePorts");

            migrationBuilder.CreateTable(
                name: "Registers",
                columns: table => new
                {
                    RegisterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisterAddress = table.Column<int>(type: "int", nullable: false),
                    RegisterLength = table.Column<int>(type: "int", nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scale = table.Column<double>(type: "float", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsHealthy = table.Column<bool>(type: "bit", nullable: false),
                    DevicePortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registers", x => x.RegisterId);
                    table.ForeignKey(
                        name: "FK_Registers_DevicePorts_DevicePortId",
                        column: x => x.DevicePortId,
                        principalTable: "DevicePorts",
                        principalColumn: "DevicePortId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DevicePorts_DeviceId_PortIndex",
                table: "DevicePorts",
                columns: new[] { "DeviceId", "PortIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registers_DevicePortId_RegisterAddress",
                table: "Registers",
                columns: new[] { "DevicePortId", "RegisterAddress" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Registers");

            migrationBuilder.DropIndex(
                name: "IX_DevicePorts_DeviceId_PortIndex",
                table: "DevicePorts");

            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "DevicePorts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RegisterAddress",
                table: "DevicePorts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RegisterLength",
                table: "DevicePorts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Scale",
                table: "DevicePorts",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "DevicePorts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DevicePorts_DeviceId_PortIndex",
                table: "DevicePorts",
                columns: new[] { "DeviceId", "PortIndex" });
        }
    }
}
