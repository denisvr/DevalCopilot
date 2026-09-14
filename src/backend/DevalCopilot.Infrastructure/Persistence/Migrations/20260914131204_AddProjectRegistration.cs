using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevalCopilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_projects_CanonicalPath",
                table: "projects");

            migrationBuilder.AddColumn<int>(
                name: "NextBaselineNumber",
                table: "projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Permanently nullable: null honestly represents "unknown" for a legacy project
            // that predates this slice's registration flow, never a fabricated historical date.
            // Every project registered through Project.Register always supplies a real value.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RegisteredAtUtc",
                table: "projects",
                type: "TEXT",
                nullable: true);

            // Added nullable first, backfilled below for the one known legacy shape, then
            // tightened to NOT NULL — which fails this migration closed (a standard SQLite
            // constraint violation during the table rebuild the AlterColumn below performs) if
            // any OTHER, unanticipated legacy row remains. That failure is deliberate: rather
            // than inventing an identity key for a row this migration cannot verify — which
            // could permit a duplicate registration of the same path under a different key — an
            // unexpected row must be resolved by a human before the application can start.
            migrationBuilder.AddColumn<string>(
                name: "RegistrationIdentityKey",
                table: "projects",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql(BuildLegacyFixtureBackfillSql());

            migrationBuilder.AlterColumn<string>(
                name: "RegistrationIdentityKey",
                table: "projects",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "repository_baselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaselineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    HeadState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    HeadCommitSha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsDirty = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_baselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_repository_baselines_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_projects_RegistrationIdentityKey",
                table: "projects",
                column: "RegistrationIdentityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_repository_baselines_ProjectId_BaselineNumber",
                table: "repository_baselines",
                columns: new[] { "ProjectId", "BaselineNumber" },
                unique: true);
        }

        /// <summary>
        /// Backfills the identity key for the one and only pre-existing row this migration can
        /// ever encounter: ProjectFixture's single seeded walking-skeleton project. No
        /// registration path existed before this slice, so this is the complete, closed set of
        /// possible pre-existing data — never a general assumption. The key is computed with the
        /// exact same .NET normalization <c>Project.Register</c> uses (<c>ToUpperInvariant</c>),
        /// evaluated once here in C# and embedded as a literal — deliberately never SQLite's
        /// ASCII-only <c>UPPER()</c>, which could otherwise silently diverge from .NET's
        /// Unicode-aware semantics for a differently-shaped path. <c>RegisteredAtUtc</c> is left
        /// null for this row — its true registration time is genuinely unknown, not fabricated.
        /// No <c>RepositoryBaseline</c> row is created for it either: this legacy project was
        /// never actually inspected, and the read model renders a project with zero baselines as
        /// "not yet validated" rather than pretending otherwise.
        /// <para>
        /// The single-quote escaping below guards against SQL-string syntax breakage from a
        /// literal apostrophe in the fixed, hardcoded, non-attacker-controlled constant — not
        /// against injection, since no external input ever reaches this migration.
        /// </para>
        /// </summary>
        private static string BuildLegacyFixtureBackfillSql()
        {
            const string legacyFixtureCanonicalPath = @"C:\repos\DevalCopilot";
            var identityKey = legacyFixtureCanonicalPath.ToUpperInvariant().Replace("'", "''");
            var canonicalPath = legacyFixtureCanonicalPath.Replace("'", "''");

            return $"""
                UPDATE projects
                SET RegistrationIdentityKey = '{identityKey}'
                WHERE CanonicalPath = '{canonicalPath}' AND RegistrationIdentityKey IS NULL;
                """;
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_baselines");

            migrationBuilder.DropIndex(
                name: "IX_projects_RegistrationIdentityKey",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "NextBaselineNumber",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "RegisteredAtUtc",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "RegistrationIdentityKey",
                table: "projects");

            migrationBuilder.CreateIndex(
                name: "IX_projects_CanonicalPath",
                table: "projects",
                column: "CanonicalPath",
                unique: true);
        }
    }
}
