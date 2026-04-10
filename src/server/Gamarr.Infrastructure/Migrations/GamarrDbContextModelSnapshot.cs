using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
partial class GamarrDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.4");

        modelBuilder.Entity("Gamarr.Domain.Entities.MachineMount", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<DateTimeOffset?>("CompletedAtUtc").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("timestamp with time zone");
            b.Property<string>("DriveLetter").HasMaxLength(8).HasColumnType("character varying(8)");
            b.Property<string>("ErrorMessage").HasMaxLength(2048).HasColumnType("character varying(2048)");
            b.Property<string>("IsoPath").IsRequired().HasMaxLength(1024).HasColumnType("character varying(1024)");
            b.Property<Guid>("MachineId").HasColumnType("uuid");
            b.Property<string>("Status").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            b.HasKey("Id");
            b.HasIndex("MachineId", "Status");
            b.ToTable("MachineMounts");
        });
    }
}
