using Microsoft.Extensions.AI;

namespace Chater.AI.Tools;

public sealed class WorkspaceToolProvider(WorkspaceFileSystemTool fileSystem) : IChatToolProvider
{
    public Task<IEnumerable<ChatToolRegistration>> GetTools()
    {
        ChatToolRegistration[] registrations =
        [
            Create("get_workspace_entries", fileSystem.GetWorkspaceEntries, "正在获取工作空间…"),
            Create("read_workspace_file", fileSystem.ReadFileAsync, "正在读取文件…"),
            Create("write_workspace_file", fileSystem.WriteFileAsync, "正在写入文件…"),
            Create("create_workspace_directory", fileSystem.CreateDirectory, "正在创建文件夹…"),
            Create("list_workspace_directory", fileSystem.ListDirectory, "正在获取文件夹内容…")
        ];
        return Task.FromResult<IEnumerable<ChatToolRegistration>>(registrations);
    }

    private static ChatToolRegistration Create(string name, Delegate implementation, string fallbackNotice) =>
        new(name,
            AIFunctionFactory.Create(implementation, new AIFunctionFactoryOptions { Name = name }),
            call => FormatPathNotice(call, fallbackNotice));

    private static string FormatPathNotice(FunctionCallContent call, string fallback)
    {
        if (call.Arguments is not null && call.Arguments.TryGetValue("path", out var path) &&
            path?.ToString() is { Length: > 0 } pathText)
        {
            return $"{fallback.TrimEnd('…')}：{pathText}";
        }

        return fallback;
    }
}
