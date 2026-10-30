using System.Text.Json;
using Avalonia.Headless.XUnit;
using WMCL.App.Services;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

public sealed class TaskHistoryPersistenceTests
{
    [Fact]
    public void TaskHistoryRecord_SerializesAndDeserializes()
    {
        var record = new TaskHistoryRecord
        {
            Title = "安装 Fabric",
            Detail = "安装完成",
            Progress = 100,
            PercentText = "完成",
            State = "Completed",
            Weight = 1
        };
        var json = JsonSerializer.Serialize(record);
        var restored = JsonSerializer.Deserialize<TaskHistoryRecord>(json);
        Assert.NotNull(restored);
        Assert.Equal("安装 Fabric", restored.Title);
        Assert.Equal("Completed", restored.State);
        Assert.Equal(100, restored.Progress);
    }

    [Fact]
    public void TaskHistoryRecord_RoundTripsThroughToTaskEntry()
    {
        var record = new TaskHistoryRecord
        {
            Title = "安装整合包",
            Detail = "已安装",
            Progress = 100,
            PercentText = "完成",
            State = "Completed",
            Weight = 3,
            Children = new List<TaskHistoryRecord>
            {
                new() { Title = "下载 mod", Detail = "完成", Progress = 100, PercentText = "完成", State = "Completed" }
            }
        };
        var entry = record.ToTaskEntry();
        Assert.Equal("安装整合包", entry.Title);
        Assert.Equal(TaskTerminalState.Completed, entry.State);
        Assert.NotNull(entry.Children);
        Assert.Single(entry.Children);
        Assert.Equal("下载 mod", entry.Children[0].Title);
    }

    [Fact]
    public void TaskHistoryRecord_ParseState_MapsAllStates()
    {
        Assert.Equal(TaskTerminalState.Completed, TaskHistoryRecord.ParseState("Completed"));
        Assert.Equal(TaskTerminalState.Failed, TaskHistoryRecord.ParseState("Failed"));
        Assert.Equal(TaskTerminalState.Canceled, TaskHistoryRecord.ParseState("Canceled"));
        Assert.Equal(TaskTerminalState.Ended, TaskHistoryRecord.ParseState("Ended"));
        Assert.Equal(TaskTerminalState.Running, TaskHistoryRecord.ParseState("Unknown"));
    }

    [Fact]
    public void TaskHistoryRecord_SerializesChildren()
    {
        var records = new List<TaskHistoryRecord>
        {
            new() { Title = "Task 1", State = "Completed", Children = new List<TaskHistoryRecord>
            {
                new() { Title = "Child 1", State = "Failed" }
            }}
        };
        var json = JsonSerializer.Serialize(records);
        var restored = JsonSerializer.Deserialize<List<TaskHistoryRecord>>(json);
        Assert.NotNull(restored);
        Assert.Single(restored);
        var children = restored[0].Children;
        Assert.NotNull(children);
        Assert.Equal("Child 1", children[0].Title);
        Assert.Equal("Failed", children[0].State);
    }

    [Fact]
    public void TaskHistoryRecord_IgnoresNullChildren()
    {
        var record = new TaskHistoryRecord { Title = "Simple", State = "Ended" };
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        Assert.DoesNotContain("children", json);
    }

    [Fact]
    public void TaskHistoryRecord_V2Fields_RoundTrip()
    {
        var record = new TaskHistoryRecord
        {
            Title = "安装 1.21.1",
            Detail = "安装失败：网络中断",
            Progress = 42,
            PercentText = "失败",
            State = "Failed",
            TaskId = "20261030-120000-000001-安装 1.21.1",
            FinishedAt = DateTimeOffset.Parse("2026-10-30T12:00:05+08:00"),
            Failure = new TaskFailureRecord
            {
                ExceptionType = "System.Net.WebException",
                Message = "超时",
                StackTrace = "   at Install()",
                CapturedAt = DateTimeOffset.Parse("2026-10-30T12:00:04+08:00")
            },
            Logs = new List<TaskLogRecord>
            {
                new() { Timestamp = DateTimeOffset.Now, Level = "Info", Stage = "安装", Message = "下载 50%" }
            },
            Retry = new TaskRetryRecord
            {
                Kind = "VersionInstall",
                GameRoot = "/tmp/game",
                VersionId = "1.21.1",
                Options = new Dictionary<string, string> { ["mirror"] = "Auto" }
            }
        };

        var json = JsonSerializer.Serialize(record);
        var restored = JsonSerializer.Deserialize<TaskHistoryRecord>(json);

        Assert.NotNull(restored);
        Assert.Equal(record.TaskId, restored.TaskId);
        Assert.Equal(record.FinishedAt, restored.FinishedAt);
        Assert.NotNull(restored.Failure);
        Assert.Equal("System.Net.WebException", restored.Failure.ExceptionType);
        Assert.Equal("   at Install()", restored.Failure.StackTrace);
        Assert.NotNull(restored.Logs);
        Assert.Single(restored.Logs);
        Assert.Equal("下载 50%", restored.Logs[0].Message);
        Assert.NotNull(restored.Retry);
        Assert.Equal("VersionInstall", restored.Retry.Kind);
        Assert.Equal("1.21.1", restored.Retry.VersionId);
        Assert.Equal("Auto", restored.Retry.Options!["mirror"]);

        var entry = restored.ToTaskEntry();
        Assert.Equal(record.TaskId, entry.TaskId);
        Assert.NotNull(entry.Failure);
        Assert.True(entry.IsRetryable);
        Assert.Single(entry.Logs!);
        Assert.Equal(TaskRetryKind.VersionInstall, entry.Retry!.Kind);
    }

    [Fact]
    public void TaskHistoryRecord_V1Json_LoadsWithDefaultNewFields()
    {
        const string v1Json = """
            [{"title":"安装 Fabric","detail":"安装完成","progress":100,"percent":"完成","state":"Completed","weight":1}]
            """;

        var restored = JsonSerializer.Deserialize<List<TaskHistoryRecord>>(v1Json);

        Assert.NotNull(restored);
        Assert.Single(restored);
        var record = restored[0];
        Assert.Equal("安装 Fabric", record.Title);
        Assert.Equal("Completed", record.State);
        Assert.Null(record.TaskId);
        Assert.Null(record.FinishedAt);
        Assert.Null(record.Failure);
        Assert.Null(record.Logs);
        Assert.Null(record.Retry);
        var entry = record.ToTaskEntry();
        Assert.Null(entry.TaskId);
        Assert.False(entry.IsRetryable);
        Assert.Equal(TaskTerminalState.Completed, entry.State);
    }

    [Fact]
    public void TaskRetryRecord_UnknownKind_DegradesToNull()
    {
        var record = new TaskRetryRecord { Kind = "NotAKnownKind", GameRoot = "/tmp" };
        Assert.Null(record.ToTaskRetryPayload());
    }

    private sealed class HistoryFixture : IDisposable
    {
        public readonly string DataDir = Path.Combine(
            Path.GetTempPath(), "wmcl-history-" + Guid.NewGuid().ToString("N"));
        public readonly string Root;
        public readonly string HistoryFile;
        private readonly string? _oldXdg;

        public HistoryFixture()
        {
            _oldXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", DataDir);
            Root = Path.Combine(DataDir, "game");
            Directory.CreateDirectory(Root);
            HistoryFile = Path.Combine(DataDir, "history.json");
        }

        public TaskManagerViewModel Create(out DownloadViewModel download)
        {
            var services = new AppServices(new AppSettings { GameDirectory = Root });
            download = new DownloadViewModel(services);
            var mods = new ModsViewModel(services);
            return new TaskManagerViewModel(
                download, mods, new ModpackViewModel(services),
                new InstanceContentViewModel(services), new InstanceViewModel(services),
                new InstallViewModel(download, mods, services),
                historyFilePath: HistoryFile);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldXdg);
            try
            {
                Directory.Delete(DataDir, true);
            }
            catch
            {
            }
        }
    }

    [AvaloniaFact]
    public async Task SaveHistory_WritesV2DocumentAtomically()
    {
        using var fixture = new HistoryFixture();
        var tasks = fixture.Create(out var download);

        download.AcceptInstanceCatalog(new[] { new VersionItem { Id = "1.21.1" } });
        download.SelectedVersion = download.Versions.First(v => v.Id == "1.21.1");

        using (GameOperationCoordinator.Shared.Acquire(fixture.Root, "测试占用"))
        {
            await download.InstallCommand.ExecuteAsync(null);
        }

        Assert.True(File.Exists(fixture.HistoryFile));
        Assert.False(File.Exists(fixture.HistoryFile + ".tmp"));
        var json = await File.ReadAllTextAsync(fixture.HistoryFile);
        Assert.StartsWith("{", json.TrimStart());
        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.Contains("\"taskId\"", json);
        Assert.Contains("\"failure\"", json);
        Assert.Contains("\"retry\"", json);
    }

    [AvaloniaFact]
    public void Dispose_MarksRunningTasksCanceledAndSaves()
    {
        using var fixture = new HistoryFixture();
        var tasks = fixture.Create(out var download);
        download.AcceptInstanceCatalog(new[] { new VersionItem { Id = "1.21.1" } });
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();
        Assert.Single(tasks.Tasks);

        tasks.Dispose();

        Assert.True(File.Exists(fixture.HistoryFile));
        var document = JsonSerializer.Deserialize<TaskHistoryDocument>(
            File.ReadAllText(fixture.HistoryFile));
        Assert.NotNull(document);
        Assert.Equal(2, document.SchemaVersion);
        var record = Assert.Single(document.Records);
        Assert.Equal("Canceled", record.State);
        // 二次 Dispose 幂等，不重复写盘
        tasks.Dispose();
    }

    [AvaloniaFact]
    public void LoadHistory_V1ArrayFile_StillLoads()
    {
        using var fixture = new HistoryFixture();
        File.WriteAllText(fixture.HistoryFile,
            """[{"title":"旧任务","detail":"完成","progress":100,"percent":"完成","state":"Completed","weight":1}]""");

        var tasks = fixture.Create(out _);

        var history = Assert.Single(tasks.History);
        Assert.Equal("旧任务", history.Title);
        Assert.Equal(TaskTerminalState.Completed, history.State);
        Assert.Equal("", tasks.HistoryStatus);
    }

    [AvaloniaFact]
    public void LoadHistory_RunningRecord_BecomesCanceled()
    {
        using var fixture = new HistoryFixture();
        File.WriteAllText(fixture.HistoryFile,
            """{"schemaVersion":2,"records":[{"title":"中断任务","detail":"下载中","progress":30,"percent":"30%","state":"Running","weight":1}]}""");

        var tasks = fixture.Create(out _);

        var history = Assert.Single(tasks.History);
        Assert.Equal(TaskTerminalState.Canceled, history.State);
    }

    [AvaloniaFact]
    public void LoadHistory_CorruptFile_FallsBackToEmptyWithStatus()
    {
        using var fixture = new HistoryFixture();
        File.WriteAllText(fixture.HistoryFile, "{ this is not valid json !!!");

        var tasks = fixture.Create(out _);

        Assert.Empty(tasks.History);
        Assert.Equal("历史不可用，已重置", tasks.HistoryStatus);

    }

    [AvaloniaFact]
    public void ClearHistory_CleansRunStore()
    {
        using var fixture = new HistoryFixture();
        var runs = new TaskRunStore();
        var services = new AppServices(new AppSettings { GameDirectory = fixture.Root });
        var download = new DownloadViewModel(services);
        var mods = new ModsViewModel(services);
        var tasks = new TaskManagerViewModel(
            download, mods, new ModpackViewModel(services),
            new InstanceContentViewModel(services), new InstanceViewModel(services),
            new InstallViewModel(download, mods, services),
            historyFilePath: fixture.HistoryFile, runs: runs);

        download.AcceptInstanceCatalog(new[] { new VersionItem { Id = "1.21.1" } });
        download.ProgressText = "下载 50%";
        download.IsBusy = true;
        download.CanCancel = true;
        tasks.Refresh();
        var taskId = Assert.Single(tasks.Tasks).TaskId!;
        Assert.True(runs.GetLogCount(taskId) > 0);

        download.IsBusy = false;
        download.CanCancel = false;
        tasks.Refresh();
        Assert.Single(tasks.History);

        tasks.ClearHistoryCommand.Execute(null);

        Assert.Empty(tasks.History);
        Assert.Empty(runs.GetLogs(taskId));
        Assert.Null(runs.GetFailure(taskId));
    }
}
