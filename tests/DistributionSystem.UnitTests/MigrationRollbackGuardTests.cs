using DistributionSystem.Infrastructure.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DistributionSystem.UnitTests;

public class MigrationRollbackGuardTests
{
    [Theory]
    [InlineData("Personal", "UserRecordTrashes", "UserRecordTrashes")]
    [InlineData("Quotation", "Quotations", "IsPurgedByAdmin")]
    [InlineData("Payment", "RepPayments", "IsPurgedByAdmin")]
    public void Protected_downgrades_require_sql_guard_before_dropping_visibility_state(
        string migrationKind, string table, string stateColumn)
    {
        Migration migration = migrationKind switch
        {
            "Personal" => new AddUserRecordTrash(),
            "Quotation" => new AddQuotationAdminPurgeAudit(),
            "Payment" => new AddRepPaymentAdminPurgeAudit(),
            _ => throw new ArgumentOutOfRangeException(nameof(migrationKind))
        };

        var operations = migration.DownOperations.ToList();
        operations.Should().NotBeEmpty();
        var guard = operations[0].Should().BeOfType<SqlOperation>().Subject;
        guard.Sql.Should().Contain("RAISE EXCEPTION");
        guard.Sql.Should().Contain("Unsafe Trash rollback");
        guard.Sql.Should().Contain(table);
        guard.Sql.Should().Contain(stateColumn);

        operations.Skip(1).Should().Contain(operation =>
            operation is DropTableOperation || operation is DropColumnOperation);
    }
}
