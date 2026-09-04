namespace DTMS.SharedKernel.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class BusinessRuleViolationException : Exception
{
    public BusinessRuleViolationException(string message) : base(message) { }
}

/// <summary>
/// A uniqueness pre-check rejected the write before it reached the database.
/// Maps to 409 in the API's exception middleware — the same status the raw
/// SQLSTATE 23505 arm produces when a concurrent insert wins the race, so a
/// caller sees one status for one condition. Unlike that arm, the message is
/// authored by the handler and names the offending value.
/// </summary>
public class DuplicateValueException : Exception
{
    public DuplicateValueException(string message) : base(message) { }
}
