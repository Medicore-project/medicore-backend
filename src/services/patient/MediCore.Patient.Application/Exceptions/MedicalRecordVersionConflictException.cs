namespace MediCore.Patient.Application.Exceptions;

public sealed class MedicalRecordVersionConflictException : Exception
{
    public MedicalRecordVersionConflictException(Exception? innerException = null)
        : base("The medical record was updated by another request.", innerException)
    {
    }
}
