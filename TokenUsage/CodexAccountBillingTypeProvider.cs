using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CodexDiscordPresence;

internal interface IBillingTypeProvider
{
    string? GetBillingType(CancellationToken cancellationToken = default);
}

internal interface IRateLimitProvider
{
    RateLimitSnapshot? GetRateLimit(CancellationToken cancellationToken = default);
}

internal sealed class CodexAccountBillingTypeProvider : IBillingTypeProvider, IRateLimitProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ActiveRefreshInterval = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan IdleRefreshInterval = TimeSpan.FromMinutes(5);
    private const string CodexExecutable = "codex.cmd";
    private readonly string _codexHomePath;
    private readonly object _snapshotLock = new();
    private readonly Func<DateTime> _utcNow;
    private readonly Func<CancellationToken, Task<AccountSnapshot>> _readSnapshot;
    private AccountSnapshot _cachedSnapshot = new(null, null);
    private Task<AccountSnapshot>? _refreshTask;
    private DateTime _refreshStartedUtc;
    private DateTime _lastReadUtc = DateTime.MinValue;
    private DateTime _retryAfterUtc = DateTime.MinValue;
    private int _failedAttempts;
    private bool _isActive;

    public CodexAccountBillingTypeProvider(string codexHomePath)
    {
        _codexHomePath = codexHomePath;
        _utcNow = () => DateTime.UtcNow;
        _readSnapshot = token => Task.Run(() => ReadAccountSnapshot(token), token);
    }

    internal CodexAccountBillingTypeProvider(
        string codexHomePath,
        Func<CancellationToken, Task<AccountSnapshot>> readSnapshot,
        Func<DateTime> utcNow)
    {
        _codexHomePath = codexHomePath;
        _readSnapshot = readSnapshot;
        _utcNow = utcNow;
    }

    internal void SetActivity(CodexActivityKind activityKind)
    {
        lock (_snapshotLock) _isActive = activityKind.IsActive();
    }

    public string? GetBillingType(CancellationToken cancellationToken = default)
    {
        return GetAccountSnapshot(cancellationToken).BillingType;
    }

    public RateLimitSnapshot? GetRateLimit(CancellationToken cancellationToken = default)
    {
        return GetAccountSnapshot(cancellationToken).RateLimit;
    }

    private AccountSnapshot GetAccountSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_snapshotLock)
        {
            var nowUtc = _utcNow();
            CompleteRefreshIfReady(nowUtc);
            var interval = _isActive ? ActiveRefreshInterval : IdleRefreshInterval;
            if (_refreshTask is null && nowUtc >= _retryAfterUtc && nowUtc - _lastReadUtc >= interval)
            {
                // Account IPC/HTTP is auxiliary; the presence loop always returns immediately.
                _refreshStartedUtc = nowUtc;
                _refreshTask = StartRefresh(cancellationToken);
                CompleteRefreshIfReady(nowUtc);
            }
            return _cachedSnapshot;
        }
    }

    private Task<AccountSnapshot> StartRefresh(CancellationToken cancellationToken)
    {
        try { return _readSnapshot(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Task.FromResult(new AccountSnapshot(null, null)); }
    }

    private void CompleteRefreshIfReady(DateTime nowUtc)
    {
        if (_refreshTask is null || !_refreshTask.IsCompleted) return;
        var refresh = _refreshTask;
        _refreshTask = null;
        if (refresh.IsCanceled) return;
        AccountSnapshot snapshot;
        try { snapshot = refresh.GetAwaiter().GetResult(); }
        catch { snapshot = new AccountSnapshot(null, null); }
        _lastReadUtc = _refreshStartedUtc;
        _cachedSnapshot = snapshot.BillingType is null
            ? _cachedSnapshot with { RateLimit = null }
            : snapshot;
        if (snapshot.BillingType is null || (snapshot.BillingType == "subsc" && snapshot.RateLimit is null))
        {
            _failedAttempts = Math.Min(_failedAttempts + 1, 5);
            _retryAfterUtc = nowUtc.AddMinutes(Math.Min(15, 1 << (_failedAttempts - 1)));
        }
        else
        {
            _failedAttempts = 0;
            _retryAfterUtc = DateTime.MinValue;
        }
    }

    private AccountSnapshot ReadAccountSnapshot(CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            process = StartAppServer();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            process.StandardInput.AutoFlush = true;
            WriteRequest(
                process,
                new
                {
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        clientInfo = new
                        {
                            name = "codex-discord-presence",
                            title = ProductBrand.Name,
                            version = AppVersion.Current.ToString()
                        },
                        capabilities = new
                        {
                            experimentalApi = true
                        }
                    }
                });

            if (ReadResponse(process, 1, timeout.Token) is null)
            {
                return new AccountSnapshot(null, null);
            }

            WriteRequest(process, new { method = "initialized", @params = new { } });
            WriteRequest(
                process,
                new
                {
                    id = 2,
                    method = "account/read",
                    @params = new
                    {
                        refreshToken = false
                    }
                });

            var accountResponse = ReadResponse(process, 2, timeout.Token);
            var billingType = accountResponse.HasValue
                ? ResolveBillingType(accountResponse.Value)
                : null;
            var rateLimit = billingType == "subsc"
                ? ReadFiveHourRateLimit(process, timeout.Token)
                : null;

            return new AccountSnapshot(billingType, rateLimit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AccountSnapshot(null, null);
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                }

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                process.Dispose();
            }
        }
    }

    private static RateLimitSnapshot? ReadFiveHourRateLimit(
        Process process,
        CancellationToken cancellationToken)
    {
        WriteRequest(
            process,
            new
            {
                id = 3,
                method = "account/rateLimits/read"
            });

        var response = ReadResponse(process, 3, cancellationToken);
        return response.HasValue ? ResolveFiveHourRateLimit(response.Value) : null;
    }

    internal static string? ResolveBillingType(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("account", out var account) ||
            !account.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return type.GetString()?.Trim().ToLowerInvariant() switch
        {
            "apikey" or "api_key" => "API",
            "chatgpt" or "chatgptauthtokens" => "subsc",
            _ => null
        };
    }

    internal static RateLimitSnapshot? ResolveFiveHourRateLimit(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result))
        {
            return null;
        }

        if (result.TryGetProperty("rateLimitsByLimitId", out var rateLimitsByLimitId) &&
            rateLimitsByLimitId.ValueKind == JsonValueKind.Object)
        {
            foreach (var rateLimit in rateLimitsByLimitId.EnumerateObject())
            {
                if (TryReadFiveHourRateLimit(rateLimit.Value, out var snapshot))
                {
                    return snapshot;
                }
            }
        }

        return result.TryGetProperty("rateLimits", out var rateLimits) &&
            TryReadFiveHourRateLimit(rateLimits, out var fallbackSnapshot)
            ? fallbackSnapshot
            : null;
    }

    private static bool TryReadFiveHourRateLimit(
        JsonElement rateLimit,
        out RateLimitSnapshot snapshot)
    {
        snapshot = default!;

        if (!rateLimit.TryGetProperty("primary", out var primary) ||
            primary.ValueKind != JsonValueKind.Object ||
            !TryGetInt32(primary, "usedPercent", out var usedPercent) ||
            !TryGetInt32(primary, "windowDurationMins", out var windowDurationMinutes) ||
            windowDurationMinutes != 300 ||
            !TryGetInt64(primary, "resetsAt", out var resetsAt))
        {
            return false;
        }

        try
        {
            snapshot = new RateLimitSnapshot(
                usedPercent,
                windowDurationMinutes,
                DateTimeOffset.FromUnixTimeSeconds(resetsAt).UtcDateTime);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private Process StartAppServer()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CodexExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.Environment["CODEX_HOME"] = _codexHomePath;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginErrorReadLine();
        return process;
    }

    private static void WriteRequest(Process process, object request)
    {
        process.StandardInput.WriteLine(JsonSerializer.Serialize(request));
    }

    private static JsonElement? ReadResponse(Process process, int requestId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
            if (line is null)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.Number ||
                    !id.TryGetInt32(out var responseId) ||
                    responseId != requestId)
                {
                    continue;
                }

                return root.Clone();
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    internal sealed record AccountSnapshot(
        string? BillingType,
        RateLimitSnapshot? RateLimit);
}

public sealed record RateLimitSnapshot(
    int UsedPercent,
    int WindowDurationMinutes,
    DateTime ResetAtUtc);
