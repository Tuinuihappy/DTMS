using DTMS.SharedKernel.Auth;

namespace DTMS.Fleet.Application.Services;

/// <summary>
/// Resolves the current actor to a name worth writing into an audit column.
///
/// <para><see cref="ICurrentActorContext.Current"/> never returns null — it falls
/// back to <see cref="ActorContext.System"/>, whose <c>DisplayName</c> can be an
/// empty string. Writing that into a NOT NULL column succeeds (empty is not
/// null) and leaves a history row that identifies nobody, so the fallback chain
/// is made explicit here instead of being rediscovered per handler.</para>
/// </summary>
public static class ActorName
{
    public static string Of(ICurrentActorContext actor)
    {
        var current = actor.Current;

        if (!string.IsNullOrWhiteSpace(current.DisplayName))
            return current.DisplayName.Trim();

        // PrincipalId is prefixed (user:{employeeId} / system:{key}) so it still
        // identifies who acted even without a display name.
        if (!string.IsNullOrWhiteSpace(current.PrincipalId))
            return current.PrincipalId.Trim();

        return "system";
    }
}
