namespace WMCL.App.ViewModels.Instances;

/// <summary>实例的显示身份：名称、描述、分类、收藏与图标。</summary>
public readonly record struct InstanceIdentity(string Description, string Category, bool Favorite, string LogoPath)
{
    public const string Uncategorized = "未分类";

    public static InstanceIdentity From(string? description, string? category, bool favorite, string? logoPath) =>
        new(description ?? "",
            string.IsNullOrWhiteSpace(category) ? Uncategorized : category.Trim(),
            favorite,
            logoPath ?? "");

    /// <summary>落盘时「未分类」存为空串。</summary>
    public string StoredCategory => Category == Uncategorized ? "" : Category;

    public string TrimmedDescription => Description.Trim();
    public string TrimmedLogoPath => LogoPath.Trim();
}
