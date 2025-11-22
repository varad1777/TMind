using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class somechanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceSlaves_DeviceSlaveSets_PortSetId",
                table: "DeviceSlaves");

            migrationBuilder.DropIndex(
                name: "IX_DeviceSlaves_PortSetId",
                table: "DeviceSlaves");

            migrationBuilder.DropColumn(
                name: "PortSetId",
                table: "DeviceSlaves");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceSlaves_Devices_DeviceId",
                table: "DeviceSlaves",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "DeviceId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceSlaves_Devices_DeviceId",
                table: "DeviceSlaves");

            migrationBuilder.AddColumn<Guid>(
                name: "PortSetId",
                table: "DeviceSlaves",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSlaves_PortSetId",
                table: "DeviceSlaves",
                column: "PortSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceSlaves_DeviceSlaveSets_PortSetId",
                table: "DeviceSlaves",
                column: "PortSetId",
                principalTable: "DeviceSlaveSets",
                principalColumn: "PortSetId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
