using Microsoft.AspNetCore.Authorization;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.3.1a — authorization requirement keyed by a template that
/// contains the literal placeholder <c>{key}</c>. The matching
/// <see cref="SourceSystemPermissionHandler"/> resolves the placeholder
/// from the request's route value at enforcement time, so a single
/// endpoint registration covers every system slug — admin can add a
/// new <c>SystemClient</c> to the DB without redeploying.
///
/// <para>Sibling of <see cref="PermissionRequirement"/> — both live on
/// federated <c>/api/v1/source/*</c> endpoints, but only this one
/// derives the permission code from the URL.</para>
/// </summary>
public sealed class SourceSystemPermissionRequirement : IAuthorizationRequirement
{
    public string Template { get; }

    public SourceSystemPermissionRequirement(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("Template is required.", nameof(template));
        if (!template.Contains("{key}", StringComparison.Ordinal))
            throw new ArgumentException(
                "Template must contain the literal '{key}' placeholder.", nameof(template));

        Template = template;
    }
}
