using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexDiscordPresence.PreviewCapture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var outputPath = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("Preview", "dashboard-test.png"));
        var provider = args.Length > 1 ? args[1] : ProviderIds.Codex;
        if (provider == "claude") provider = ProviderIds.ClaudeCode;
        if (provider is not (ProviderIds.Codex or ProviderIds.ClaudeCode or ProviderIds.Antigravity or "none"))
            throw new ArgumentException("Use codex, claude-code, antigravity, or none.", nameof(args));
        var connection = args.Length > 2 ? args[2] : "connected";
        if (connection is not ("connected" or "connecting" or "disconnected" or "unacknowledged" or "disabled"))
            throw new ArgumentException("Unknown connection fixture.", nameof(args));
        var dpi = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 96;
        if (dpi is < 96 or > 288) throw new ArgumentOutOfRangeException(nameof(dpi));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var runtime = new PresenceRuntimeState { Enabled = connection != "disabled" };
        runtime.InitializeProviderEnabled(new Dictionary<string, bool>
        {
            [ProviderIds.Codex] = true, [ProviderIds.ClaudeCode] = true, [ProviderIds.Antigravity] = true
        });
        runtime.PublishDashboardSnapshot(CreateShowcaseSnapshot(provider, connection));
        using var form = new CodexDashboardForm(runtime);
        form.Show();
        PumpMessages(TimeSpan.FromMilliseconds(100));
        var baseDpi = form.DeviceDpi;
        var baseClientSize = form.ClientSize;
        if (dpi != form.DeviceDpi) ExerciseDpiChange(form, dpi);
        SetSimulatedChildDpi(form, dpi);
        // Physical non-client borders stay at monitor DPI. Normalize the fixture's
        // client area explicitly so drawing comparisons retain the intended proportions.
        var timer = typeof(CodexDashboardForm).GetField("_refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form) as System.Windows.Forms.Timer ?? throw new InvalidOperationException("Refresh timer missing.");
        timer.Stop();
        form.MinimumSize = Size.Empty;
        var targetClientSize = new Size((int)Math.Round(baseClientSize.Width * dpi / (float)baseDpi),
            (int)Math.Round(baseClientSize.Height * dpi / (float)baseDpi));
        form.ClientSize = targetClientSize;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GetClientRect(form.Handle, out var clientRect);
            if (clientRect.Right == targetClientSize.Width && clientRect.Bottom == targetClientSize.Height) break;
            SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, form.Width + targetClientSize.Width - clientRect.Right,
                form.Height + targetClientSize.Height - clientRect.Bottom, 0x0016);
        }
        GetClientRect(form.Handle, out var actualClient);
        if (actualClient.Right != targetClientSize.Width || actualClient.Bottom != targetClientSize.Height)
            throw new InvalidOperationException("DPI fixture client size did not match.");
        PumpMessages(TimeSpan.FromMilliseconds(100));
        using var bitmap = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(outputPath, ImageFormat.Png);
        Console.WriteLine($"Captured sanitized dashboard: {outputPath}; {form.Width}x{form.Height}; simulated WinForms DPI={dpi}; physical window DPI={GetDpiForWindow(form.Handle)}.");
        foreach (var control in Descendants(form))
            Console.WriteLine($"{control.GetType().Name}: DPI={control.DeviceDpi}; bounds={control.Bounds}.");
        var layout = form.Controls.OfType<TableLayoutPanel>().Single();
        var preview = layout.GetControlFromPosition(0, 2) ?? throw new InvalidOperationException("Preview row missing.");
        using var previewBitmap = new Bitmap(preview.Width, preview.Height, PixelFormat.Format32bppArgb);
        preview.DrawToBitmap(previewBitmap, preview.ClientRectangle);
        previewBitmap.Save(Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath) + "-preview.png"), ImageFormat.Png);
        form.Close();
        return 0;
    }

    private static PresenceDashboardSnapshot CreateShowcaseSnapshot(string provider, string connection)
    {
        // Hand-authored presentation fixtures never read user logs, configuration, or account data.
        var now = DateTime.UtcNow;
        var state = provider switch
        {
            ProviderIds.ClaudeCode => "Editing DashboardControls.cs",
            ProviderIds.Antigravity => "Planning a UI update",
            _ => "MCP chrome-devtools"
        };
        var details = provider == ProviderIds.Codex ? "gpt 6.1 sol high" : provider == ProviderIds.ClaudeCode ? "claude sonnet high" : "gemini 3.1 pro high";
        var small = provider == ProviderIds.ClaudeCode ? "clawd-notification" : provider == ProviderIds.Antigravity ? "rpc_antigravity_cli" : "rpc_codex";
        var usage = provider == ProviderIds.Codex ? new TokenUsageSnapshot(null, null, "subsc", new(25, 300, now.AddHours(3)))
            : provider == ProviderIds.Antigravity ? new TokenUsageSnapshot(null, null, PlanName: "Pro", UsageQuotas: [new("model", 0.75m, now.AddHours(2))])
            : new TokenUsageSnapshot(null, null);
        var rendered = new RenderedPresence(details, state, "rpc_reading", small, [], now.AddMinutes(-2),
            CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "");
        return new PresenceDashboardSnapshot(AppProfileKind.Codex, details, "presence-dashboard", rendered, usage,
            connection is "connected" or "unacknowledged" or "disabled", now)
        {
            ProviderId = provider == "none" ? null : provider,
            HasNoActiveProvider = provider == "none",
            IsDiscordConnecting = connection == "connecting",
            PublishedPresence = connection is "unacknowledged" or "disabled" || provider == "none" ? null : new DiscordPresenceSnapshot(
                details, state, provider == ProviderIds.ClaudeCode ? "clawd-working-typing" : provider == ProviderIds.Antigravity ? "rpc_antigravity_cli" : "rpc_reading", state,
                small, provider, now.AddMinutes(-2), null, null, [])
        };
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static void SetSimulatedChildDpi(Form form, int dpi)
    {
        // Test-only: WM_DPICHANGED cannot change the actual monitor DPI, so Windows
        // leaves child caches at the physical DPI. Set their caches for drawing fixtures.
        var property = typeof(Control).GetProperty("DeviceDpiInternal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(Control).FullName, "DeviceDpiInternal");
        foreach (var control in Descendants(form))
        {
            property.SetValue(control, dpi);
            if (control.DeviceDpi != dpi) throw new InvalidOperationException("DPI fixture cache did not update.");
        }
        foreach (var control in Descendants(form))
        {
            typeof(Control).GetMethod("OnResize", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(control, [EventArgs.Empty]);
            control.PerformLayout();
            control.Invalidate();
        }
    }

    private static void ExerciseDpiChange(Form form, int dpi)
    {
        // Exercise the real WinForms WM_DPICHANGED path without changing the user's monitor settings.
        var scale = dpi / (float)form.DeviceDpi;
        var suggested = new NativeRect { Left = form.Left, Top = form.Top,
            Right = form.Left + (int)Math.Round(form.Width * scale), Bottom = form.Top + (int)Math.Round(form.Height * scale) };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(suggested, pointer, false);
            SendMessage(form.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void PumpMessages(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, IntPtr data);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
