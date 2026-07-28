using System.Collections.Concurrent;

namespace DistributionSystem.Application.Services.Security;

public static class TemporaryPasswordStore
{
    private static readonly ConcurrentDictionary<Guid, string> Passwords = new();

    public static void Set(Guid userId, string temporaryPassword)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(temporaryPassword)) return;
        Passwords[userId] = temporaryPassword;
    }

    public static string? Get(Guid userId)
    {
        return Passwords.TryGetValue(userId, out var value) ? value : null;
    }

    public static void Clear(Guid userId)
    {
        Passwords.TryRemove(userId, out _);
    }
}
