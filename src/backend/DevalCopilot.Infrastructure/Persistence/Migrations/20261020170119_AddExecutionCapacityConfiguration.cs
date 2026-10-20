using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionCapacityConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execution_capacity_configurations",
                columns: table => new
                {
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Limit = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_capacity_configurations", x => x.Revision);
                    table.CheckConstraint("ck_execution_capacity_configurations_limit", "typeof(\"Limit\") = 'integer' AND \"Limit\" IN (1, 2)");
                    table.CheckConstraint("ck_execution_capacity_configurations_revision", "typeof(\"Revision\") = 'integer' AND \"Revision\" >= 1");
                });

            migrationBuilder.InsertData(
                table: "execution_capacity_configurations",
                columns: new[] { "Revision", "Limit" },
                values: new object[] { 1L, 2 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "execution_capacity_configurations");
        }
    }
}
