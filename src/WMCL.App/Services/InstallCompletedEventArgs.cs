namespace WMCL.App.Services;

public sealed class InstallCompletedEventArgs(string gameDirectory, string instanceId, bool instanceRenamed) : EventArgs
{
    public string GameDirectory { get; } = gameDirectory;
    public string InstanceId { get; } = instanceId;
    public bool InstanceRenamed { get; } = instanceRenamed;
}
