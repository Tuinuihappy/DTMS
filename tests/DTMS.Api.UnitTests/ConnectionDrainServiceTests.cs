using DTMS.Api.Realtime.Drain;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// The drain broadcast lists its hubs by hand. OperatorPoolHub and
// ManualBoardHub were left off that list, so operator tablets never got
// "__drain" before a restart. These tests fail when a hub is added to the
// API without being added to the drain.
public class ConnectionDrainServiceTests
{
    private static readonly Type[] AllHubs = typeof(ConnectionDrainService).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Hub).IsAssignableFrom(t))
        .ToArray();

    [Fact]
    public void EveryHub_IsInTheDrainList()
    {
        var drained = typeof(ConnectionDrainService).GetConstructors().Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IHubContext<>))
            .Select(t => t.GetGenericArguments()[0])
            .ToList();

        AllHubs.Should().HaveCountGreaterThanOrEqualTo(7);
        drained.Should().BeEquivalentTo(AllHubs,
            "a hub missing here never tells its clients to reconnect before a restart");
    }

    [Fact]
    public async Task StartDrain_SendsDrainToEveryHub()
    {
        var proxies = new List<IClientProxy>();
        var args = typeof(ConnectionDrainService).GetConstructors().Single()
            .GetParameters()
            .Select(p =>
            {
                if (p.ParameterType == typeof(Microsoft.Extensions.Logging.ILogger<ConnectionDrainService>))
                    return (object)NullLogger<ConnectionDrainService>.Instance;
                var hub = Substitute.For([p.ParameterType], []);
                var proxy = Substitute.For<IClientProxy>();
                proxies.Add(proxy);
                var clients = Substitute.For<IHubClients>();
                clients.Group(Arg.Any<string>()).Returns(proxy);
                p.ParameterType.GetProperty("Clients")!.GetValue(hub).Returns(clients);
                return hub;
            })
            .ToArray();
        var sut = (ConnectionDrainService)Activator.CreateInstance(typeof(ConnectionDrainService), args)!;

        await sut.StartDrainAsync(TimeSpan.Zero);

        proxies.Should().HaveCount(AllHubs.Length);
        foreach (var proxy in proxies)
            await proxy.Received(1).SendCoreAsync("__drain", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
