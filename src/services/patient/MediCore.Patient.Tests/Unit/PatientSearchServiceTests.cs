using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientSearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 8, 30, 0, TimeSpan.Zero);
    private static readonly PatientAccessContext Access = new(
        " receptionist-1 ", " Receptionist ", "corr-25", " 127.0.0.1 ");

    [Fact]
    public async Task Search_trims_query_and_passes_pagination_to_repository()
    {
        var fixture = new Fixture(SearchResponse());

        await fixture.Service.SearchAsync(new PatientSearchRequest("  Perera  ", 2, 10), Access);

        Assert.Equal("Perera", fixture.Search.LastSearchTerm);
        Assert.Equal(2, fixture.Search.LastPage);
        Assert.Equal(10, fixture.Search.LastPageSize);
    }

    [Fact]
    public async Task Search_audits_each_patient_returned_and_saves_once()
    {
        var response = SearchResponse(2);
        var fixture = new Fixture(response);

        var result = await fixture.Service.SearchAsync(new PatientSearchRequest("Perera", 1, 20), Access);

        Assert.Same(response, result);
        Assert.Equal(2, fixture.Audits.Added.Count);
        Assert.All(fixture.Audits.Added, audit =>
        {
            Assert.Equal("PatientSearchResultViewed", audit.Action);
            Assert.Equal("receptionist-1", audit.ActorId);
            Assert.Equal("Receptionist", audit.ActorRole);
            Assert.Equal("corr-25", audit.CorrelationId);
            Assert.Equal("127.0.0.1", audit.IpAddress);
            Assert.Equal(Now.UtcDateTime, audit.OccurredAtUtc);
        });
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Search_audit_uses_safe_fallbacks_for_missing_actor_information()
    {
        var fixture = new Fixture(SearchResponse());
        var missingAccessDetails = new PatientAccessContext(" ", "", "corr-25", null);

        await fixture.Service.SearchAsync(
            new PatientSearchRequest("Perera", 1, 20),
            missingAccessDetails);

        var audit = Assert.Single(fixture.Audits.Added);
        Assert.Equal("system", audit.ActorId);
        Assert.Equal("Unknown", audit.ActorRole);
        Assert.Null(audit.IpAddress);
    }

    [Fact]
    public async Task No_matches_returns_empty_page_without_audit_write()
    {
        var fixture = new Fixture(PatientSearchResponse.Empty(1, 20));

        var result = await fixture.Service.SearchAsync(
            new PatientSearchRequest("missing", 1, 20),
            Access);

        Assert.Empty(result.Items);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_returns_empty_page_without_querying_database(string? query)
    {
        var fixture = new Fixture(SearchResponse());

        var result = await fixture.Service.SearchAsync(
            new PatientSearchRequest(query, 3, 10),
            Access);

        Assert.Empty(result.Items);
        Assert.Equal(3, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, fixture.Search.CallCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    private static PatientSearchResponse SearchResponse(int count = 1)
    {
        var items = Enumerable.Range(1, count)
            .Select(index => new PatientSearchResult(
                Guid.NewGuid(),
                $"PAT-{index:000000}",
                $"20001234567{index}",
                $"Patient {index}",
                new DateOnly(2000, 1, index),
                "0771234567",
                $"patient{index}@example.com",
                "Colombo"))
            .ToList();

        return new PatientSearchResponse(items, count, 1, 20, 1, false, false);
    }

    private sealed class Fixture
    {
        public Fixture(PatientSearchResponse response)
        {
            Search = new FakeSearchRepository(response);
            Audits = new FakeAuditRepository();
            UnitOfWork = new FakeUnitOfWork();
            Service = new PatientSearchService(
                Search,
                Audits,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }

        public FakeSearchRepository Search { get; }
        public FakeAuditRepository Audits { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public PatientSearchService Service { get; }
    }

    private sealed class FakeSearchRepository : IPatientSearchRepository
    {
        private readonly PatientSearchResponse _response;

        public FakeSearchRepository(PatientSearchResponse response)
        {
            _response = response;
        }

        public int CallCount { get; private set; }
        public string? LastSearchTerm { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }

        public Task<PatientSearchResponse> SearchAsync(
            string searchTerm,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSearchTerm = searchTerm;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(_response);
        }
    }

    private sealed class FakeAuditRepository : IPatientAuditRepository
    {
        public List<PatientAuditLog> Added { get; } = [];

        public Task AddAsync(PatientAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Added.Add(auditLog);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task SaveRegistrationAsync(string duplicateNic, CancellationToken cancellationToken = default) =>
            SaveChangesAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
