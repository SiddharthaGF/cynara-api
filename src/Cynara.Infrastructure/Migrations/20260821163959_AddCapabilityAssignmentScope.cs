using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cynara.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilityAssignmentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded DDL on purpose: databases baselined after the legacy
            // schema DDL was aligned with this change already carry the Scope
            // column and the partial indexes, while databases baselined
            // before it do not. The IF NOT EXISTS / IF EXISTS guards make
            // both paths converge on the same schema without failing.
            migrationBuilder.Sql("""
                ALTER TABLE "capability_assignments"
                    ADD COLUMN IF NOT EXISTS "Scope"
                        character varying(16) NOT NULL DEFAULT 'hospital';

                DROP INDEX IF EXISTS
                    "IX_capability_assignments_HospitalId_ActorId_Capability";

                CREATE UNIQUE INDEX IF NOT EXISTS
                    "IX_capability_assignments_HospitalId_ActorId_Capability"
                    ON "capability_assignments" ("HospitalId", "ActorId", "Capability")
                    WHERE "Scope" = 'hospital';

                CREATE UNIQUE INDEX IF NOT EXISTS
                    "IX_capability_assignments_ActorId_Capability"
                    ON "capability_assignments" ("ActorId", "Capability")
                    WHERE "Scope" = 'platform';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the pre-scope schema: plain unique index on the
            // hospital triple, then drops the Scope column last so the index
            // swap never references a missing column.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS
                    "IX_capability_assignments_ActorId_Capability";

                DROP INDEX IF EXISTS
                    "IX_capability_assignments_HospitalId_ActorId_Capability";

                CREATE UNIQUE INDEX IF NOT EXISTS
                    "IX_capability_assignments_HospitalId_ActorId_Capability"
                    ON "capability_assignments" ("HospitalId", "ActorId", "Capability");

                ALTER TABLE "capability_assignments"
                    DROP COLUMN IF EXISTS "Scope";
                """);
        }
    }
}
