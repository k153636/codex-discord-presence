using System.Globalization;

namespace CodexDiscordPresence;

internal static class UpdateRestartArguments
{
    private const string SessionStartFlag = "--resume-session-start";

    public static DateTime ReadSessionStart(string[] arguments, DateTime nowUtc)
    {
        for (var i = 0; i + 1 < arguments.Length; i++)
        {
            if (arguments[i] == SessionStartFlag &&
                DateTime.TryParseExact(arguments[i + 1], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var startedAt) &&
                startedAt.Kind == DateTimeKind.Utc && startedAt <= nowUtc &&
                startedAt >= nowUtc.AddYears(-1))
            {
                return startedAt;
            }
        }
        return nowUtc;
    }

    public static string[] Build(string[] arguments, DateTime startedAtUtc)
    {
        var result = new List<string>();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == SessionStartFlag)
            {
                if (i + 1 < arguments.Length) i++;
            }
            else result.Add(arguments[i]);
        }
        result.Add(SessionStartFlag);
        result.Add(startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return result.ToArray();
    }
}
