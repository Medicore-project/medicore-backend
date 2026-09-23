using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;

namespace MediCore.Patient.Application.Services;

/// <inheritdoc cref="IPatientIdentificationService"/>
public sealed class PatientIdentificationService : IPatientIdentificationService
{
    private readonly IPatientRepository _patientRepository;
    private readonly IBookingTokenGenerator _tokenGenerator;

    public PatientIdentificationService(
        IPatientRepository patientRepository,
        IBookingTokenGenerator tokenGenerator)
    {
        _patientRepository = patientRepository;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<BookingIdentityResponse?> IdentifyAsync(
        string patientNumber,
        DateOnly dateOfBirth,
        CancellationToken cancellationToken = default)
    {
        // One query whatever goes wrong, so an unknown number and a wrong date of birth are
        // indistinguishable from the outside — in timing as well as in the response body.
        var patient = await _patientRepository.FindByPatientNumberAndDateOfBirthAsync(
            PatientInputNormalizer.PatientNumber(patientNumber),
            dateOfBirth,
            cancellationToken);

        return patient is null
            ? null
            : IssueFor(patient.Id, patient.PatientNumber, patient.FullName);
    }

    public BookingIdentityResponse IssueFor(Guid patientId, string patientNumber, string fullName)
    {
        var (token, expiresAtUtc) = _tokenGenerator.Generate(patientId, patientNumber, fullName);

        return new BookingIdentityResponse(patientId, patientNumber, fullName, token, expiresAtUtc);
    }
}
