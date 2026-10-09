using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CodexDiscordPresence;

public sealed class CodexDashboardForm : Form
{
    internal const string DashboardAddress = "https://dashboard.local/Dashboard.html";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly PresenceRuntimeState _runtimeState;
    private readonly PresenceStateStore _stateStore;
    private readonly string _statePath;
    private readonly WebView2 _webView = new() {Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 12, 14)};
    private readonly System.Windows.Forms.Timer _refreshTimer = new() {Interval = 500};
    private readonly DashboardSnapshotSelector _snapshotSelector = new();
    private PresenceDashboardSnapshot? _sentSnapshot;
    private string? _lastPayload;
    private long _snapshotId;
    private bool _ready;

    public CodexDashboardForm(PresenceRuntimeState runtimeState)
        : this(runtimeState, new PresenceStateStore(), PresenceStateStore.GetDefaultPath()) { }

    public CodexDashboardForm(PresenceRuntimeState runtimeState, PresenceStateStore stateStore, string statePath)
    {
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _statePath = !string.IsNullOrWhiteSpace(statePath) ? statePath : throw new ArgumentException("A state path is required.", nameof(statePath));
        Text = ProductBrand.Name;
        AccessibleName = $"{ProductBrand.Name} dashboard";
        StartPosition = FormStartPosition.CenterScreen;
        // The export includes its own title bar. Use its complete original canvas size.
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(402, 414);
        MinimumSize = ClientSize;
        BackColor = _webView.DefaultBackgroundColor;
        Controls.Add(_webView);
        _refreshTimer.Tick += (_, _) => RefreshSnapshot();
        Shown += async (_, _) => await InitializeDashboardAsync();
    }

    internal DashboardWebPayload CurrentPayload => DashboardWebPayload.Create(SelectSnapshot(), _runtimeState, DateTime.UtcNow);

    private async Task InitializeDashboardAsync()
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Dashboard");
            var adapter = await File.ReadAllTextAsync(Path.Combine(folder, "runtime-adapter.js"));
            if (IsDisposed) return;
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder:
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexDiscordPresence", "dashboard-webview"));
            if (IsDisposed) return;
            await _webView.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            var browser = _webView.CoreWebView2;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.IsStatusBarEnabled = false;
            browser.Settings.IsZoomControlEnabled = false;
            browser.SetVirtualHostNameToFolderMapping("dashboard.local", folder, CoreWebView2HostResourceAccessKind.DenyCors);
            browser.SetVirtualHostNameToFolderMapping("rpc-art.local", Path.Combine(AppContext.BaseDirectory, "Assets", "RpcArt"), CoreWebView2HostResourceAccessKind.DenyCors);
            browser.NavigationStarting += (_, e) => e.Cancel = e.Uri != DashboardAddress;
            browser.NewWindowRequested += (_, e) => e.Handled = true;
            browser.WebMessageReceived += OnWebMessage;
            await browser.AddScriptToExecuteOnDocumentCreatedAsync(adapter);
            if (!IsDisposed) browser.Navigate(DashboardAddress);
        }
        catch (Exception error) when (!IsDisposed)
        {
            // A missing browser runtime must not stop the tray/RPC service.
            MessageBox.Show(this, "The dashboard could not open. Install or repair Microsoft Edge WebView2 Runtime.\n\n" + error.Message,
                ProductBrand.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
        catch (Exception) when (IsDisposed) { }
    }

    private PresenceDashboardSnapshot SelectSnapshot()
    {
        var latest = _runtimeState.DashboardSnapshot;
        if (!_runtimeState.Enabled || latest.ProviderId is { } id && !_runtimeState.IsProviderEnabled(id))
        {
            _snapshotSelector.Reset();
            latest = latest with {ProviderId = null, Presence = null, PublishedPresence = null, TokenUsage = null, HasNoActiveProvider = true};
        }
        return _snapshotSelector.Select(latest, DateTime.UtcNow);
    }

    private void RefreshSnapshot()
    {
        if (!_ready || IsDisposed || Disposing) return;
        var snapshot = SelectSnapshot();
        var payload = JsonSerializer.Serialize(DashboardWebPayload.Create(snapshot, _runtimeState, DateTime.UtcNow), WebJson);
        if (payload == _lastPayload && snapshot.PublishedPresence?.PublicationGeneration == _sentSnapshot?.PublishedPresence?.PublicationGeneration) return;
        _lastPayload = payload;
        _sentSnapshot = snapshot;
        _webView.CoreWebView2.PostWebMessageAsJson($"{{\"type\":\"snapshot\",\"id\":{++_snapshotId},\"payload\":{payload}}}");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != DashboardAddress) return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            HandleMessage(document.RootElement);
        }
        catch (JsonException) { }
    }

    internal void HandleMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return;
        if (!message.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
        switch (type.GetString())
        {
            case "ready":
                _ready = true;
                _lastPayload = null;
                _refreshTimer.Start();
                RefreshSnapshot();
                break;
            case "painted" when message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var paintedId) && paintedId == _snapshotId:
                if (_sentSnapshot is { } displayed)
                {
                    _snapshotSelector.RecordOwnerDisplayed(displayed, DateTime.UtcNow);
                    _snapshotSelector.RecordPresenceDisplayed(displayed, DateTime.UtcNow);
                }
                break;
            case "provider":
                ApplyProviderMessage(message);
                break;
            case "window" when message.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String:
                ApplyWindowAction(action.GetString());
                break;
            case "button":
                var url = _sentSnapshot?.PublishedPresence?.Buttons.FirstOrDefault()?.Url;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) {UseShellExecute = true});
                break;
        }
    }

    private void ApplyProviderMessage(JsonElement message)
    {
        if (!message.TryGetProperty("providerId", out var provider) || provider.ValueKind != JsonValueKind.String ||
            !message.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        var id = provider.GetString();
        if (id is not (ProviderIds.Codex or ProviderIds.ClaudeCode or ProviderIds.Antigravity)) return;
        _runtimeState.SetProviderEnabled(id, enabled.GetBoolean());
        if (!enabled.GetBoolean() && id == _snapshotSelector.CurrentProviderId) _snapshotSelector.Reset();
        _stateStore.Save(_statePath, _runtimeState);
        RefreshSnapshot();
    }

    private void ApplyWindowAction(string? action)
    {
        switch (action)
        {
            case "close": Close(); break;
            case "minimize": WindowState = FormWindowState.Minimized; break;
            case "maximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
            case "drag":
                ReleaseCapture();
                SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
