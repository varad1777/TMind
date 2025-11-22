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
                name: "IX_DeviceSlaves_DeviceId_slaveIndex",
                table: "DeviceSlaves");

            migrationBuilder.DropColumn(
                name: "DataType",
                table: "DeviceSlaves");

            migrationBuilder.DropColumn(
                name: "RegisterAddress",
                table: "DeviceSlaves");

            migrationBuilder.DropColumn(
                name: "RegisterLength",
                table: "DeviceSlaves");

            migrationBuilder.DropColumn(
                name: "Scale",
                table: "DeviceSlaves");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "DeviceSlaves");

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
                    deviceSlaveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registers", x => x.RegisterId);
                    table.ForeignKey(
                        name: "FK_Registers_DeviceSlaves_deviceSlaveId",
                        column: x => x.deviceSlaveId,
                        principalTable: "DeviceSlaves",
                        principalColumn: "deviceSlaveId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSlaves_DeviceId_slaveIndex",
                table: "DeviceSlaves",
                columns: new[] { "DeviceId", "slaveIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registers_deviceSlaveId_RegisterAddress",
                table: "Registers",
                columns: new[] { "deviceSlaveId", "RegisterAddress" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Registers");

            migrationBuilder.DropIndex(
                name: "IX_DeviceSlaves_DeviceId_slaveIndex",
                table: "DeviceSlaves");

            migrationBuilder.AddColumn<string>(
                name: "DataType",
                table: "DeviceSlaves",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RegisterAddress",
                table: "DeviceSlaves",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RegisterLength",
                table: "DeviceSlaves",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Scale",
                table: "DeviceSlaves",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "DeviceSlaves",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSlaves_DeviceId_slaveIndex",
                table: "DeviceSlaves",
                columns: new[] { "DeviceId", "slaveIndex" });
        }
    }
}
