namespace Chater.AI.Tools;

/// <summary>Stable user-facing metadata for the tools shipped with Chater.</summary>
public sealed record ChatToolDefinition(string Name, string DisplayNameKey, string DescriptionKey);

public static class ChatToolCatalog
{
    public static IReadOnlyList<ChatToolDefinition> All { get; } =
    [
        new("fetch_webpage_content", "ToolFetchWebpage", "ToolFetchWebpageDescription"),
        new("get_workspace_entries", "ToolGetWorkspaceEntries", "ToolGetWorkspaceEntriesDescription"),
        new("read_workspace_file", "ToolReadWorkspaceFile", "ToolReadWorkspaceFileDescription"),
        new("write_workspace_file", "ToolWriteWorkspaceFile", "ToolWriteWorkspaceFileDescription"),
        new("create_workspace_directory", "ToolCreateWorkspaceDirectory", "ToolCreateWorkspaceDirectoryDescription"),
        new("list_workspace_directory", "ToolListWorkspaceDirectory", "ToolListWorkspaceDirectoryDescription")
    ];
}
