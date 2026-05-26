using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

/// <summary>
/// Translates upstream UOM strings (canonical names, aliases, or junk) into
/// the closed <see cref="UnitOfMeasure"/> enum. Returns null when the input
/// cannot be resolved; callers turn that into a 400 / Result.Failure.
/// </summary>
public interface IUomNormalizer
{
    UnitOfMeasure? Normalize(string? input);
}
