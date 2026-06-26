namespace DTMS.SharedKernel.Auth;

/// <summary>
/// Default <see cref="ICurrentActorContext"/> backed by an
/// <see cref="AsyncLocal{T}"/> stack so consumers/background-jobs can push
/// an explicit context without poisoning unrelated async flows.
///
/// In a typical HTTP request, no <c>BeginScope</c> is called — the resolver
/// reads the <c>IHttpContextAccessor</c> via the optional callback supplied
/// by the host. Host code wires the callback in <c>Program.cs</c> so this
/// SharedKernel assembly stays free of an <c>HttpContextAccessor</c>
/// dependency (it would otherwise leak ASP.NET into every module).
/// </summary>
public sealed class AsyncLocalActorContext : ICurrentActorContext
{
    private static readonly AsyncLocal<ActorContext?> _ambient = new();
    private readonly Func<ActorContext?>? _httpResolver;

    public AsyncLocalActorContext(Func<ActorContext?>? httpResolver = null)
    {
        _httpResolver = httpResolver;
    }

    public ActorContext Current
    {
        get
        {
            if (_ambient.Value is { } explicitContext)
                return explicitContext;

            if (_httpResolver?.Invoke() is { } fromHttp)
                return fromHttp;

            return ActorContext.System;
        }
    }

    public IDisposable BeginScope(ActorContext context)
    {
        var previous = _ambient.Value;
        _ambient.Value = context;
        return new RestoreScope(() => _ambient.Value = previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;
        public RestoreScope(Action onDispose) => _onDispose = onDispose;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onDispose();
        }
    }
}
