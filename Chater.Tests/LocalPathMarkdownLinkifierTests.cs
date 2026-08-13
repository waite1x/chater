using Chater.Views;

namespace Chater.Tests;

public sealed class LocalPathMarkdownLinkifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Chater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Linkify_ConvertsExistingAbsolutePathAndPreservesFollowingText()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "a file.txt");
        File.WriteAllText(path, "content");

        var markdown = LocalPathMarkdownLinkifier.Linkify($"已写入 {path}，请打开查看。");

        Assert.Contains($"[{path}]({new Uri(path).AbsoluteUri})", markdown);
        Assert.EndsWith("，请打开查看。", markdown);
    }

    [Fact]
    public void Linkify_LeavesNonexistentPathAndExistingMarkdownLinkUntouched()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "existing.txt");
        File.WriteAllText(path, "content");
        var missing = Path.Combine(_root, "missing.txt");

        var markdown = LocalPathMarkdownLinkifier.Linkify($"[{path}](file:///already-linked) {missing}");

        Assert.Contains($"[{path}](file:///already-linked)", markdown);
        Assert.Contains(missing, markdown);
        Assert.DoesNotContain($"[{missing}]", markdown);
    }

    [Fact]
    public void Linkify_ConvertsExistingInlineCodePathToClickableLink()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "inline.txt");
        File.WriteAllText(path, "content");

        var markdown = LocalPathMarkdownLinkifier.Linkify($"打开 `{path}`。");

        Assert.Contains($"[{path}]({new Uri(path).AbsoluteUri})", markdown);
        Assert.DoesNotContain($"`{path}`", markdown);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
