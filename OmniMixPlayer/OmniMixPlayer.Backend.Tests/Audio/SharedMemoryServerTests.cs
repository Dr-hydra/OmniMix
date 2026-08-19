using System;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using OmniMixPlayer.Backend.Audio;
using OmniMixPlayer.SDK.Ipc;
using Xunit;

namespace OmniMixPlayer.Backend.Tests.Audio;

public sealed class SharedMemoryServerTests
{
    [Fact]
    public void EofDrainAndExplicitOperationsUseSeparateGenerations()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var server = CreateServer();

        var firstStream = server.BeginStream("track-a", 480);
        server.MarkFormatReady(48_000, 2, 480);
        server.WriteFrames(new float[480 * 2], 480);
        server.MarkDecoderEof(480);

        Assert.Equal((int)SharedMemoryStreamState.Draining,
            server.ReadI32(SharedMemoryProtocol.StreamState));
        Assert.False(server.IsClientDrained(32));

        server.WriteI64(SharedMemoryProtocol.ReadCursor, 480);
        server.WriteI64(SharedMemoryProtocol.AudibleCursor, 450);
        Assert.True(server.IsClientDrained(32));
        server.MarkEnded();
        Assert.Equal((int)SharedMemoryStreamState.Ended,
            server.ReadI32(SharedMemoryProtocol.StreamState));

        var seekStream = server.AdvanceGeneration(240);
        Assert.True(seekStream > firstStream);
        Assert.Equal(240, server.GetWriteCursor());
        Assert.Equal(240, server.GetReadCursor());
        Assert.Equal(240, server.GetAudibleCursor());

        var stoppedStream = server.StopStream();
        Assert.True(stoppedStream > seekStream);
        Assert.Equal((int)SharedMemoryStreamState.Stopped,
            server.ReadI32(SharedMemoryProtocol.StreamState));
    }

    [Fact]
    public void HeartbeatContinuesWhilePaused()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var server = CreateServer();
        server.BeginStream("paused-track", 0);
        server.SetPlayState(2);
        var before = server.ReadI64(SharedMemoryProtocol.LastUpdateTick);

        Thread.Sleep(1_200);

        var after = server.ReadI64(SharedMemoryProtocol.LastUpdateTick);
        Assert.True(after > before);
        Assert.Equal((int)SharedMemoryStreamState.Paused,
            server.ReadI32(SharedMemoryProtocol.StreamState));
    }

    [Fact]
    public void GlobalMappingFallsBackWithoutElevation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var requestedName = $@"Global\OmniMixPlayer_PCM_Test_{Guid.NewGuid():N}";
        using var server = new SharedMemoryServer(NullLogger.Instance, requestedName);
        Assert.True(server.Initialize());
        Assert.True(
            string.Equals(server.ActualMapName, requestedName, StringComparison.OrdinalIgnoreCase) ||
            server.ActualMapName.StartsWith(@"Local\", StringComparison.OrdinalIgnoreCase));
    }

    private static SharedMemoryServer CreateServer()
    {
        var mapName = $@"Local\OmniMixPlayer_PCM_Test_{Guid.NewGuid():N}";
        var server = new SharedMemoryServer(NullLogger.Instance, mapName);
        Assert.True(server.Initialize());
        return server;
    }
}
