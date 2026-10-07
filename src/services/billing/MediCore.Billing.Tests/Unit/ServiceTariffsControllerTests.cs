using MediCore.Billing.Api.Controllers;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Tests.Unit;

public sealed class ServiceTariffsControllerTests
{
    [Fact]
    public async Task Duplicate_service_code_is_returned_as_409()
    {
        var controller = Controller(new StubTariffService
        {
            CreateResult = new DuplicateServiceTariffCodeResult("GEN-CONSULT")
        });

        var result = await controller.Create(
            new CreateServiceTariffRequest(
                "GEN-CONSULT",
                "General consultation",
                2500m,
                "LKR",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Contains("GEN-CONSULT", problem.Title);
    }

    [Fact]
    public async Task Invalid_tariff_is_returned_as_400_before_service_call()
    {
        var service = new StubTariffService();
        var controller = Controller(service);

        var result = await controller.Create(
            new CreateServiceTariffRequest("bad code", "", 0m, "x", DateTime.MinValue),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, service.CreateCalls);
    }

    private static ServiceTariffsController Controller(StubTariffService service) =>
        new(service, new CreateServiceTariffRequestValidator(), new UpdateServiceTariffRequestValidator());

    private sealed class StubTariffService : IServiceTariffService
    {
        public CreateServiceTariffResult CreateResult { get; set; } =
            new DuplicateServiceTariffCodeResult("TEST");
        public int CreateCalls { get; private set; }

        public Task<IReadOnlyList<ServiceTariffResponse>> GetAllAsync(
            bool includeInactive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceTariffResponse>>([]);

        public Task<ServiceTariffResponse?> GetByIdAsync(
            Guid tariffId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ServiceTariffResponse?>(null);

        public Task<CreateServiceTariffResult> CreateAsync(
            CreateServiceTariffRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(CreateResult);
        }

        public Task<UpdateServiceTariffResult> UpdatePriceAsync(
            Guid tariffId,
            UpdateServiceTariffRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateServiceTariffResult>(new ServiceTariffNotFoundResult());

        public Task<DeactivateServiceTariffResult> DeactivateAsync(
            Guid tariffId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DeactivateServiceTariffResult.NotFound);
    }
}
