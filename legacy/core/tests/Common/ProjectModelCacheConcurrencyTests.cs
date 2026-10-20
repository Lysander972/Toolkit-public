using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("FileIO")]
public sealed class ProjectModelCacheConcurrencyTests
{
    [Fact]
    public async Task ListDevices_Should_Not_See_Empty_During_Concurrent_Rebuild()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        (ProjectRootEntity rootA, string _) = BuildRoot("main", 54, 1001);
        (ProjectRootEntity rootB, string _) = BuildRoot("main", 54, 2002);
        model.SetRootForTesting(rootA);

        int sawEmpty = 0;
        using ManualResetEventSlim start = new ManualResetEventSlim(false);
        using CancellationTokenSource stop = new CancellationTokenSource();

        Task writer = Task.Run(() =>
        {
            start.Wait();
            for (int i = 0; i < 6000; i++)
            {
                ProjectRootEntity root = rootB;
                if ((i & 1) == 0)
                {
                    root = rootA;
                }
                model.SetRootForTesting(root);
            }
            stop.Cancel();
        });

        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait();
            while (!stop.IsCancellationRequested)
            {
                if (model.ListDevices().Count == 0)
                {
                    Interlocked.Exchange(ref sawEmpty, 1);
                    stop.Cancel();
                    return;
                }
            }
        })).ToArray();

        start.Set();
        await writer;
        await Task.WhenAll(readers);

        Assert.Equal(0, Volatile.Read(ref sawEmpty));
    }

    [Fact]
    public async Task TryGet_Should_Not_Miss_Stable_Device_Path_During_Concurrent_Rebuild()
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        (ProjectRootEntity rootA, string deviceRelativePath) = BuildRoot("main", 54, 3003);
        (ProjectRootEntity rootB, string _) = BuildRoot("main", 54, 4004);
        model.SetRootForTesting(rootA);
        int sawMiss = 0;
        using ManualResetEventSlim start = new ManualResetEventSlim(false);
        using CancellationTokenSource stop = new CancellationTokenSource();

        Task writer = Task.Run(() =>
        {
            start.Wait();
            for (int i = 0; i < 6000; i++)
            {
                ProjectRootEntity root = rootB;
                if ((i & 1) == 0)
                {
                    root = rootA;
                }
                model.SetRootForTesting(root);
            }
            stop.Cancel();
        });

        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.Wait();
            while (!stop.IsCancellationRequested)
            {
                if (!model.EntityExists(deviceRelativePath))
                {
                    Interlocked.Exchange(ref sawMiss, 1);
                    stop.Cancel();
                    return;
                }
            }
        })).ToArray();

        start.Set();
        await writer;
        await Task.WhenAll(readers);

        Assert.Equal(0, Volatile.Read(ref sawMiss));
    }

    private static (ProjectRootEntity Root, string DeviceRelativePath) BuildRoot(string mapName, int deviceId, int deviceType)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, mapName);
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId, deviceType);
        return (root, device.device目录_完整相对路径);
    }
}
