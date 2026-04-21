namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetMapById;

public record MapDto(
    Guid Id,
    string Name,
    string Version,
    double Width,
    double Height,
    string MapData);
