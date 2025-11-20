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
                name: "FK_DevicePorts_DevicePortSets_PortSetId",
                table: "DevicePorts");

            migrationBuilder.DropIndex(
                name: "IX_DevicePorts_PortSetId",
                table: "DevicePorts");

            migrationBuilder.DropColumn(
                name: "PortSetId",
                table: "DevicePorts");

            migrationBuilder.AddForeignKey(
                name: "FK_DevicePorts_Devices_DeviceId",
                table: "DevicePorts",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "DeviceId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DevicePorts_Devices_DeviceId",
                table: "DevicePorts");

            migrationBuilder.AddColumn<Guid>(
                name: "PortSetId",
                table: "DevicePorts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_DevicePorts_PortSetId",
                table: "DevicePorts",
                column: "PortSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_DevicePorts_DevicePortSets_PortSetId",
                table: "DevicePorts",
                column: "PortSetId",
                principalTable: "DevicePortSets",
                principalColumn: "PortSetId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
