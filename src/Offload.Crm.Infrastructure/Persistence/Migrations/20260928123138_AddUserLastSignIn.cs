using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Offload.Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSignInAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSignInAt",
                table: "AspNetUsers");
        }
    }
}
