namespace AMR.DeliveryPlanning.Planning.Domain.Enums;

// DTMS-local coarse category for an ActionTemplate (STD vs ACT). Distinct
// from the RIOT3 wire `actionType` string (e.g. "standardRobotsCustom"),
// which lives on ActionTemplate.ActionType.
//
// Wire format uses uppercase tokens ("STD", "ACT") via
// JsonStringEnumConverter(SnakeCaseUpper) configured in Program.cs, and the
// DB column stays varchar(50) via HasConversion<string>() so existing rows
// ("STD", "ACT") round-trip without a data migration.
public enum ActionCategory
{
    Std,
    Act
}
