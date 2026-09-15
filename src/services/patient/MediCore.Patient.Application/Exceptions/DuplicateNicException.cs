namespace MediCore.Patient.Application.Exceptions;

public sealed class DuplicateNicException : Exception
{
    public DuplicateNicException(string nic, Exception? innerException = null)
        : base($"A patient with NIC '{nic}' already exists.", innerException)
    {
        Nic = nic;
    }

    public string Nic { get; }
}
