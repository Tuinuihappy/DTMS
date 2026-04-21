namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public interface IRouteSolver
{
    /// <summary>
    /// Given a start station and a list of drop stations, return the optimal visit sequence.
    /// </summary>
    List<Guid> SolveRoute(Guid startStation, List<Guid> dropStations);
}
