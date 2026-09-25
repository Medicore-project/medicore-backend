using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

/// <summary>
/// Lets a returning patient prove who they are without an account, so they can book.
/// </summary>
public interface IPatientIdentificationService
{
    /// <summary>
    /// The patient matching this number and date of birth, with a fresh booking token, or
    /// <c>null</c> if there is no match.
    /// </summary>
    /// <remarks>
    /// A single nullable return rather than a result union, deliberately. There is exactly one
    /// failure here and the caller must not be able to tell an unknown patient number from a wrong
    /// date of birth — distinguishing them would turn a sequential, guessable patient number into
    /// a way to confirm which numbers exist.
    /// </remarks>
    Task<BookingIdentityResponse?> IdentifyAsync(
        string patientNumber,
        DateOnly dateOfBirth,
        CancellationToken cancellationToken = default);

    /// <summary>A booking token for a patient who has just been registered.</summary>
    BookingIdentityResponse IssueFor(Guid patientId, string patientNumber, string fullName);
}
