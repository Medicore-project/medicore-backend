namespace MediCore.Identity.Application.Interfaces;

public interface IReportQuery<TFilter, TRow>
{
    Task<IReadOnlyList<TRow>> QueryAsync(TFilter filter, CancellationToken cancellationToken = default);
}
