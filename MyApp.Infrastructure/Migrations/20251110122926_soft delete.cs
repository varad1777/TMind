using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class softdelete : Migration
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
                name: "DevicePortSets",
                columns: table => new
                {
                    PortSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePortSets", x => x.PortSetId);
                    table.ForeignKey(
                        name: "FK_DevicePortSets_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DevicePorts",
                columns: table => new
                {
                    DevicePortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortIndex = table.Column<int>(type: "int", nullable: false),
                    RegisterAddress = table.Column<int>(type: "int", nullable: false),
                    RegisterLength = table.Column<int>(type: "int", nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scale = table.Column<double>(type: "float", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsHealthy = table.Column<bool>(type: "bit", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePorts", x => x.DevicePortId);
                    table.ForeignKey(
                        name: "FK_DevicePorts_DevicePortSets_PortSetId",
                        column: x => x.PortSetId,
                        principalTable: "DevicePortSets",
                        principalColumn: "PortSetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DevicePorts_DeviceId_PortIndex",
                table: "DevicePorts",
                columns: new[] { "DeviceId", "PortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_DevicePorts_PortSetId",
                table: "DevicePorts",
                column: "PortSetId");

            migrationBuilder.CreateIndex(
                name: "IX_DevicePortSets_DeviceId",
                table: "DevicePortSets",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceConfigurationId",
                table: "Devices",
                column: "DeviceConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevicePorts");

            migrationBuilder.DropTable(
                name: "DevicePortSets");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "DeviceConfigurations");
        }
    }
}
