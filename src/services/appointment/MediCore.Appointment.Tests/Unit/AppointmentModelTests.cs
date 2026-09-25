using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentModelTests
{
    [Fact]
    public void Appointment_maps_to_appointments_with_a_soft_delete_filter()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity));

        Assert.NotNull(entityType);
        Assert.Equal("appointments", entityType.GetTableName());
        Assert.Equal(AppointmentDbContext.SchemaName, entityType.GetSchema());
        Assert.NotNull(entityType.GetQueryFilter());
    }

    [Fact]
    public void Appointment_id_is_the_unique_business_key()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity))!;

        var index = Assert.Single(entityType.GetIndexes(), i =>
            i.GetDatabaseName() == "ux_appointments_appointment_id");

        Assert.True(index.IsUnique);
        Assert.Equal([nameof(AppointmentEntity.AppointmentId)], index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void One_active_appointment_per_slot_is_what_actually_prevents_double_booking()
    {
        // Booking mutates a slot rather than inserting one, so ux_slots_doctor_start can never
        // fire on a concurrent booking: both racers read Available and both write Booked to the
        // same row successfully. This index is the only thing that makes one of them lose, and
        // AppointmentUnitOfWork matches it by name — so the filter is asserted byte for byte.
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity))!;

        var index = Assert.Single(entityType.GetIndexes(), i =>
            i.GetDatabaseName() == "ux_appointments_slot");

        Assert.True(index.IsUnique);
        Assert.Equal([nameof(AppointmentEntity.SlotId)], index.Properties.Select(p => p.Name));
        Assert.Equal("\"IsDeleted\" = false AND \"Status\" <> 'Cancelled'", index.GetFilter());
    }

    [Fact]
    public void The_patient_overlap_check_is_indexed_on_patient_then_start()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity))!;

        var index = Assert.Single(entityType.GetIndexes(), i =>
            i.GetDatabaseName() == "ix_appointments_patient_start");

        Assert.Equal(
            [nameof(AppointmentEntity.PatientId), nameof(AppointmentEntity.StartUtc)],
            index.Properties.Select(p => p.Name));
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void The_clinic_list_is_indexed_on_date_then_doctor()
    {
        // Date first, so the clinic-wide list uses the index as well as the per-doctor grid.
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity))!;

        var index = Assert.Single(entityType.GetIndexes(), i =>
            i.GetDatabaseName() == "ix_appointments_date_doctor");

        Assert.Equal(
            [nameof(AppointmentEntity.SlotDate), nameof(AppointmentEntity.DoctorId)],
            index.Properties.Select(p => p.Name));
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void A_new_appointment_is_booked_and_generally_billed()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity))!;

        Assert.Equal(
            AppointmentStatus.Booked,
            entityType.FindProperty(nameof(AppointmentEntity.Status))!.GetDefaultValue());
        Assert.Equal(
            ServiceCodes.GeneralConsultation,
            entityType.FindProperty(nameof(AppointmentEntity.ServiceCode))!.GetDefaultValue());
    }

    [Fact]
    public void The_patient_snapshot_is_optional_and_sized_to_the_patient_service_columns()
    {
        // Optional because a staff API booking with a bare patientId has nothing to copy.
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AppointmentEntity))!;

        var number = entityType.FindProperty(nameof(AppointmentEntity.PatientNumber))!;
        var name = entityType.FindProperty(nameof(AppointmentEntity.PatientName))!;

        Assert.True(number.IsNullable);
        Assert.Equal(20, number.GetMaxLength());
        Assert.True(name.IsNullable);
        Assert.Equal(256, name.GetMaxLength());
    }

    [Theory]
    [InlineData(ServiceCodes.GeneralConsultation)]
    [InlineData(ServiceCodes.SpecialistConsultation)]
    [InlineData(ServiceCodes.FollowUp)]
    public void Every_listed_service_code_is_known(string code)
    {
        Assert.True(ServiceCodes.IsKnown(code));
        Assert.Contains(code, ServiceCodes.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOPE")]
    // Case-sensitive on purpose: the code is stored and published verbatim, so accepting a second
    // spelling would put two names for one service on the topic.
    [InlineData("gen-consult")]
    public void Anything_not_on_the_list_is_unknown(string? code)
    {
        Assert.False(ServiceCodes.IsKnown(code));
    }

    private static AppointmentDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppointmentDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options);
}
