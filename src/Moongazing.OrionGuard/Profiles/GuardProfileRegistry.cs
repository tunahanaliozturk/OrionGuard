using System.Collections.Concurrent;

namespace Moongazing.OrionGuard.Profiles;

public static class GuardProfileRegistry
{
    private static readonly ConcurrentDictionary<string, Delegate> _profiles = new();

    public static void Register<T>(string name, Action<T, string> profile)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[name] = profile;
    }

    public static bool TryExecute<T>(string name, T value, string parameterName)
    {
        if (_profiles.TryGetValue(name, out var profile) && profile is Action<T, string> typed)
        {
            typed(value, parameterName);
            return true;
        }
        return false;
    }

    public static void Execute<T>(string name, T value, string parameterName)
    {
        if (!TryExecute(name, value, parameterName))
        {
            throw new InvalidOperationException($"No guard profile found with name '{name}' and type '{typeof(T)}'.");
        }
    }

    public static bool IsRegistered(string name) => _profiles.ContainsKey(name);

    public static bool Remove(string name) => _profiles.TryRemove(name, out _);
}