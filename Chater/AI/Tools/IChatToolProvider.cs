namespace Chater.AI.Tools;

public interface IChatToolProvider
{
    Task<IEnumerable<ChatToolRegistration>> GetTools();
}

