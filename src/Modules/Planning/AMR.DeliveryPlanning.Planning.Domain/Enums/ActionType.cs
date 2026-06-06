namespace AMR.DeliveryPlanning.Planning.Domain.Enums;

// RIOT3 action-template category. Wire format uses uppercase tokens
// ("STD", "ACT") via JsonStringEnumConverter(SnakeCaseUpper) configured in
// Program.cs, and the DB column stays varchar(50) via HasConversion<string>()
// so existing rows ("STD", "ACT") round-trip without a data migration.
public enum ActionType
{
    Std,
    Act
}
