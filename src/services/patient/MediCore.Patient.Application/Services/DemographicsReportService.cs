using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;

namespace MediCore.Patient.Application.Services;

public sealed class DemographicsReportService : IDemographicsReportService
{
    private readonly IDemographicsReportQuery _query;
    private readonly TimeProvider _timeProvider;

    public DemographicsReportService(
        IDemographicsReportQuery query,
        TimeProvider timeProvider)
    {
        _query = query;
        _timeProvider = timeProvider;
    }

    public async Task<DemographicsReportResponse> GenerateAsync(
        DemographicsReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var rows = await _query.QueryAsync(filter, cancellationToken);
        var patients = rows
            .GroupBy(row => row.PatientId)
            .Select(CreatePatientSummary)
            .OrderBy(patient => patient.PatientNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visits = rows
            .Where(row => row.VisitRecordId.HasValue && row.VisitAtUtc.HasValue)
            .GroupBy(row => row.VisitRecordId!.Value)
            .Select(group => group.First())
            .ToArray();

        var patientVisitCounts = visits
            .GroupBy(visit => visit.PatientId)
            .ToDictionary(group => group.Key, group => group.Count());

        var totalVisits = visits.Length;
        var patientsWithVisits = patientVisitCounts.Count;

        return new DemographicsReportResponse(
            GeneratedAtUtc: _timeProvider.GetUtcNow().UtcDateTime,
            AppliedFilters: CreateAppliedFilters(filter),
            TotalPatients: patients.Length,
            TotalVisits: totalVisits,
            PatientsWithVisits: patientsWithVisits,
            PatientsWithoutVisits: patients.Length - patientsWithVisits,
            AgeBands: CreateFixedBreakdown(
                DemographicsReportOptions.AgeBands,
                patients,
                patient => patient.AgeBand),
            Genders: CreateFixedBreakdown(
                DemographicsReportOptions.Genders,
                patients,
                patient => patient.Gender),
            Districts: patients
                .GroupBy(patient => patient.District, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DemographicsBreakdownRow(
                    group.First().District,
                    group.Count(),
                    group.Sum(patient => patient.VisitCount)))
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            VisitHistory: visits
                .GroupBy(visit => new { visit.VisitAtUtc!.Value.Year, visit.VisitAtUtc.Value.Month })
                .OrderBy(group => group.Key.Year)
                .ThenBy(group => group.Key.Month)
                .Select(group => new VisitHistoryBucket(
                    $"{group.Key.Year:0000}-{group.Key.Month:00}",
                    new DateOnly(group.Key.Year, group.Key.Month, 1),
                    group.Count()))
                .ToArray(),
            Patients: patients);
    }

    private static DemographicsPatientSummary CreatePatientSummary(
        IGrouping<Guid, DemographicsReportSourceRow> group)
    {
        var patient = group.First();
        var visits = group
            .Where(row => row.VisitRecordId.HasValue && row.VisitAtUtc.HasValue)
            .GroupBy(row => row.VisitRecordId!.Value)
            .Select(records => records.First().VisitAtUtc!.Value)
            .OrderBy(date => date)
            .ToArray();

        return new DemographicsPatientSummary(
            patient.PatientId,
            patient.PatientNumber,
            patient.Age,
            patient.AgeBand,
            patient.Gender,
            patient.District,
            visits.Length,
            visits.Length == 0 ? null : visits[0],
            visits.Length == 0 ? null : visits[^1]);
    }

    private static IReadOnlyList<DemographicsBreakdownRow> CreateFixedBreakdown(
        IReadOnlyList<string> labels,
        IReadOnlyCollection<DemographicsPatientSummary> patients,
        Func<DemographicsPatientSummary, string> selector) =>
        labels
            .Select(label =>
            {
                var matchingPatients = patients
                    .Where(patient => string.Equals(selector(patient), label, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                return new DemographicsBreakdownRow(
                    label,
                    matchingPatients.Length,
                    matchingPatients.Sum(patient => patient.VisitCount));
            })
            .ToArray();

    private static AppliedDemographicsFilters CreateAppliedFilters(DemographicsReportFilter filter) => new(
        AgeBand: string.IsNullOrWhiteSpace(filter.AgeBand) ? null : filter.AgeBand.Trim(),
        Gender: string.IsNullOrWhiteSpace(filter.Gender)
            ? null
            : DemographicsReportOptions.NormalizeGender(filter.Gender),
        District: string.IsNullOrWhiteSpace(filter.District) ? null : filter.District.Trim(),
        filter.From,
        filter.To);
}
