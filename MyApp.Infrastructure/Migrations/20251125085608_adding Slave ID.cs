using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addingSlaveID : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceConfigurations",
                columns: table => new
                {
                    ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PollIntervalMs = table.Column<int>(type: "int", nullable: false),
                    ProtocolSettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConfigurations", x => x.ConfigurationId);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Protocol = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeviceConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_Devices_DeviceConfigurations_DeviceConfigurationId",
                        column: x => x.DeviceConfigurationId,
                        principalTable: "DeviceConfigurations",
                        principalColumn: "ConfigurationId");
                });

            migrationBuilder.CreateTable(
                name: "DeviceSlaves",
                columns: table => new
                {
                    deviceSlaveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slaveIndex = table.Column<int>(type: "int", nullable: false),
                    IsHealthy = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSlaves", x => x.deviceSlaveId);
                    table.ForeignKey(
                        name: "FK_DeviceSlaves_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Registers",
                columns: table => new
                {
                    RegisterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisterAddress = table.Column<int>(type: "int", nullable: false),
                    RegisterLength = table.Column<int>(type: "int", nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Scale = table.Column<double>(type: "float", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsHealthy = table.Column<bool>(type: "bit", nullable: false),
                    ByteOrder = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WordSwap = table.Column<bool>(type: "bit", nullable: false),
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
                name: "IX_Devices_DeviceConfigurationId",
                table: "Devices",
                column: "DeviceConfigurationId");

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

            migrationBuilder.DropTable(
                name: "DeviceSlaves");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "DeviceConfigurations");
        }
    }
}
