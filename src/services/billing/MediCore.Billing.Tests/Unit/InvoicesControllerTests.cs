using System.Security.Claims;
using MediCore.Billing.Api.Controllers;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Tests.Unit;

public sealed class InvoicesControllerTests
{
    [Fact]
    public async Task Overpayment_is_returned_as_400_with_the_remaining_balance()
    {
        var paymentService = new StubPaymentService
        {
            Result = new PaymentOverpaymentResult(250m)
        };
        var controller = Controller(paymentService);

        var result = await controller.RecordPayment(
            Guid.NewGuid(),
            new RecordPaymentRequest(300m, "Cash"),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains("250.00", problem.Detail);
    }

    [Fact]
    public async Task Invalid_payment_method_is_returned_as_validation_400()
    {
        var paymentService = new StubPaymentService();
        var controller = Controller(paymentService);

        var result = await controller.RecordPayment(
            Guid.NewGuid(),
            new RecordPaymentRequest(100m, "Cheque"),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal(0, paymentService.CallCount);
    }

    private static InvoicesController Controller(StubPaymentService paymentService)
    {
        var controller = new InvoicesController(
            new StubInvoiceQueryService(),
            paymentService,
            new RecordPaymentRequestValidator());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Email, "reception@medicore.lk")],
                    "test"))
            }
        };
        controller.HttpContext.Items["CorrelationId"] = "corr-controller";
        return controller;
    }

    private sealed class StubInvoiceQueryService : IInvoiceQueryService
    {
        public Task<InvoiceResponse?> GetByIdAsync(
            Guid invoiceId,
            CancellationToken cancellationToken = default) => Task.FromResult<InvoiceResponse?>(null);

        public Task<InvoiceResponse?> GetByAppointmentIdAsync(
            Guid appointmentId,
            CancellationToken cancellationToken = default) => Task.FromResult<InvoiceResponse?>(null);
    }

    private sealed class StubPaymentService : IPaymentService
    {
        public RecordPaymentResult Result { get; set; } = new PaymentInvoiceNotFoundResult();
        public int CallCount { get; private set; }

        public Task<RecordPaymentResult> RecordAsync(
            Guid invoiceId,
            RecordPaymentRequest request,
            string recordedBy,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}
