namespace MediCore.Billing.Application.Entities;

public static class PaymentMethods
{
    public const string Cash = "Cash";
    public const string Card = "Card";
    public const string Insurance = "Insurance";

    public static readonly IReadOnlyList<string> All = [Cash, Card, Insurance];

    public static string? Normalize(string? method) => method?.Trim().ToUpperInvariant() switch
    {
        "CASH" => Cash,
        "CARD" => Card,
        "INSURANCE" => Insurance,
        _ => null
    };
}
