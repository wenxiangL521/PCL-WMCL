namespace WMCL.Modding.Modpack;

/// <summary>整合包安装器向宿主请求 Java 时携带的加载器上下文。</summary>
public sealed record ModpackJavaRequest(
    string Minecraft,
    string Loader,
    string? LoaderVersion,
    int SuggestedMajor);
