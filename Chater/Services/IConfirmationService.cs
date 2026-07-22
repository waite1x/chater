namespace Chater.Services;

public interface IConfirmationService
{
    Task<bool> ConfirmDeleteAsync(string itemName);
}
