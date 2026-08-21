using Avalonia.Controls;
using Chater.AI.Conversations;
using Chater.AI;
using Chater.ViewModels;
using Chater.Views;
using Markdig;
using Markdig.Syntax;

namespace Chater.Tests;

public sealed class ChatMessageViewModelTests
{
    [Fact]
    public void ThinkingBlock_UsesConcreteExpanderForThemeTemplate()
    {
        const string markdown = "````thinking\n分析请求\n````\n";
        var parsed = Markdown.Parse(markdown);
        var fencedBlock = Assert.IsType<FencedCodeBlock>(Assert.Single(parsed));
        Assert.Equal("thinking", fencedBlock.Info?.ToString().Trim());

        var expanderField = typeof(ThinkingBlockControl).GetField(
            "_expander",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(expanderField);
        Assert.Equal(typeof(Expander), expanderField.FieldType);
        Assert.False(typeof(Expander).IsAssignableFrom(typeof(ThinkingBlockControl)));
    }

    [Fact]
    public void LegacyThinkingFence_IsMovedToItsOwnLineBeforeRendering()
    {
        var content = "正文。````thinking\n分析请求\n````\n";

        Assert.Equal("正文。\n````thinking\n分析请求\n````\n", ThinkingMarkdown.NormalizeForRendering(content));
    }

    [Fact]
    public void ThinkingMarkdown_IsStoredInsideTheAssistantContent()
    {
        var content = ThinkingMarkdown.AppendReasoning("答案", "先分析请求");

        Assert.Equal("答案\n````thinking\n先分析请求\n````\n", content);
    }

    [Fact]
    public void Reasoning_IsExpandedWhileStreamingAndCollapsedWhenResponseCompletes()
    {
        var message = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);

        message.AppendReasoning("先分析请求", "思考");

        Assert.True(message.HasReasoning);
        Assert.Equal("思考", message.ThinkingStatus);

        message.CompleteResponse();

        Assert.Null(message.ThinkingStatus);
        Assert.Contains("先分析请求", message.Content);
    }

    [Fact]
    public void PersistedReasoningStartsCollapsedAndCanBeExpandedAgain()
    {
        var message = new ChatMessageViewModel(
            MessageRole.Assistant,
            ThinkingMarkdown.AppendReasoning("答案", "历史思考"));

        Assert.True(message.HasReasoning);
        Assert.True(message.HasReasoning);
    }

    [Fact]
    public void ThinkingAndToolBlocksKeepGenerationOrder()
    {
        var message = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);

        message.AppendReasoning("先分析", "思考");
        message.AppendText("先给结论");
        message.AppendToolCall("调用工具：读取工作区", "调用工具");
        message.AppendText("工具结果后的回答");

        var thinkingIndex = message.Content.IndexOf("先分析", StringComparison.Ordinal);
        var firstTextIndex = message.Content.IndexOf("先给结论", StringComparison.Ordinal);
        var toolIndex = message.Content.IndexOf("调用工具：读取工作区", StringComparison.Ordinal);
        var finalTextIndex = message.Content.IndexOf("工具结果后的回答", StringComparison.Ordinal);

        Assert.True(thinkingIndex < firstTextIndex);
        Assert.True(firstTextIndex < toolIndex);
        Assert.True(toolIndex < finalTextIndex);
    }

    [Fact]
    public void ThinkingBlocksCanBeRemovedBeforeBuildingModelContext()
    {
        var content = "问题" + ThinkingMarkdown.AppendBlock(string.Empty, "思考") + "回答";

        Assert.Equal("问题\n回答", ThinkingMarkdown.RemoveThinkingBlocks(content));
    }

    [Fact]
    public void StreamingReasoningChunksDoNotCreateArtificialLineBreaks()
    {
        var content = ThinkingMarkdown.AppendReasoning(string.Empty, "一个 ");
        content = ThinkingMarkdown.AppendReasoning(content, "连续句子");

        Assert.Contains("一个 连续句子", content);
        Assert.DoesNotContain("一个 \n连续句子", content);
    }
}
