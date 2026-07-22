using Chater.Data;
using Chater.Models;

namespace Chater.Services;

public sealed class SkillService(SkillRepository skills)
{
    public async Task<Skill> SaveCustomAsync(Skill draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SystemPrompt);
        var existing = await skills.GetByIdAsync(draft.Id, cancellationToken).ConfigureAwait(false);
        if (existing?.IsBuiltIn == true)
        {
            throw new InvalidOperationException("Built-in skills cannot be edited.");
        }

        var byName = await skills.GetByNameAsync(draft.Name, cancellationToken).ConfigureAwait(false);
        if (byName is not null && byName.Id != draft.Id)
        {
            throw new InvalidOperationException("A skill with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var saved = draft with
        {
            Name = draft.Name.Trim(),
            SystemPrompt = draft.SystemPrompt.Trim(),
            IsBuiltIn = false,
            Version = (existing?.Version ?? 0) + 1,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        await skills.SaveAsync(saved, cancellationToken).ConfigureAwait(false);
        return saved;
    }
}
