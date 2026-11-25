using MediCore.Appointment.Application.Entities;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.UnitTests.Support;

/// <summary>
/// Test Data Builders. Each test states only the field it cares about ("a slot that starts in the
/// past", "a cancelled appointment") and inherits sensible defaults for everything else, which keeps
/// the Arrange step short and the intent of each test obvious.
/// </summary>
public static class Ids
{
    public static readonly Guid Doctor = Guid.Parse("d0c70000-0000-0000-0000-000000000001");
    public static readonly Guid OtherDoctor = Guid.Parse("d0c70000-0000-0000-0000-000000000002");
    public static readonly Guid Patient = Guid.Parse("9a710000-0000-0000-0000-000000000001");
    public static readonly Guid OtherPatient = Guid.Parse("9a710000-0000-0000-0000-000000000002");
    public static readonly Guid Slot = Guid.Parse("5107a000-0000-0000-0000-000000000001");
    public static readonly Guid NewSlot = Guid.Parse("5107a000-0000-0000-0000-000000000002");
    public static readonly Guid Appointment = Guid.Parse("a9900000-0000-0000-0000-000000000001");
    public static readonly Guid Department = Guid.Parse("de970000-0000-0000-0000-000000000001");
}

public sealed class SlotBuilder
{
    private Guid _slotId = Ids.Slot;
    private Guid _doctorId = Ids.Doctor;
    private DateTime _startUtc = TestClock.Now.AddDays(1);
    private int _minutes = 30;
    private string _status = SlotStatus.Available;

    public static SlotBuilder ASlot() => new();

    public SlotBuilder WithId(Guid slotId) { _slotId = slotId; return this; }
    public SlotBuilder ForDoctor(Guid doctorId) { _doctorId = doctorId; return this; }
    public SlotBuilder StartingAt(DateTime startUtc) { _startUtc = startUtc; return this; }
    public SlotBuilder StartingIn(TimeSpan fromNow) { _startUtc = TestClock.Now + fromNow; return this; }
    public SlotBuilder LastingMinutes(int minutes) { _minutes = minutes; return this; }
    public SlotBuilder WithStatus(string status) { _status = status; return this; }

    public Slot Build() => new()
    {
        SlotId = _slotId,
        DoctorId = _doctorId,
        StartUtc = _startUtc,
        EndUtc = _startUtc.AddMinutes(_minutes),
        SlotDate = DateOnly.FromDateTime(_startUtc),
        DurationMinutes = _minutes,
        Status = _status,
        CreatedBy = "seed"
    };
}

public sealed class AppointmentBuilder
{
    private Guid _appointmentId = Ids.Appointment;
    private Guid _patientId = Ids.Patient;
    private Guid _doctorId = Ids.Doctor;
    private Guid _slotId = Ids.Slot;
    private DateTime _startUtc = TestClock.Now.AddDays(3);
    private int _minutes = 30;
    private string _status = AppointmentStatus.Booked;

    public static AppointmentBuilder AnAppointment() => new();

    public AppointmentBuilder ForPatient(Guid patientId) { _patientId = patientId; return this; }
    public AppointmentBuilder WithDoctor(Guid doctorId) { _doctorId = doctorId; return this; }
    public AppointmentBuilder OnSlot(Guid slotId) { _slotId = slotId; return this; }
    public AppointmentBuilder StartingAt(DateTime startUtc) { _startUtc = startUtc; return this; }
    public AppointmentBuilder StartingIn(TimeSpan fromNow) { _startUtc = TestClock.Now + fromNow; return this; }
    public AppointmentBuilder WithStatus(string status) { _status = status; return this; }

    public AppointmentEntity Build() => new()
    {
        AppointmentId = _appointmentId,
        PatientId = _patientId,
        DoctorId = _doctorId,
        SlotId = _slotId,
        StartUtc = _startUtc,
        EndUtc = _startUtc.AddMinutes(_minutes),
        SlotDate = DateOnly.FromDateTime(_startUtc),
        DurationMinutes = _minutes,
        Status = _status,
        CreatedBy = "seed"
    };
}

public static class Doctors
{
    public static DoctorCache Active(string specialization = "Cardiology", Guid? doctorId = null) => new()
    {
        DoctorId = doctorId ?? Ids.Doctor,
        FullName = "Dr. Anura Silva",
        Specialization = specialization,
        DepartmentId = Ids.Department,
        IsActive = true
    };
}
