using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalExecutionOrderCounterConfiguration : IEntityTypeConfiguration<LocalExecutionOrderCounter>
{
    public void Configure(EntityTypeBuilder<LocalExecutionOrderCounter> builder)
    {
        builder.ToTable("local_execution_order_counters", table =>
        {
            table.HasCheckConstraint("ck_local_execution_order_counters_singleton", "\"Id\" = 1");
            table.HasCheckConstraint("ck_local_execution_order_counters_last", "typeof(\"LastNumber\") = 'integer' AND \"LastNumber\" >= 0");
            table.HasCheckConstraint("ck_local_execution_order_counters_revision", "\"Revision\" >= 0");
        });
        builder.HasKey(counter => counter.Id);
        builder.Property(counter => counter.Id).ValueGeneratedNever();
        builder.Property(counter => counter.Revision).IsConcurrencyToken();
        builder.HasData(new { Id = LocalExecutionOrderCounter.SingletonId, LastNumber = 0L, Revision = 0L });
    }
}
