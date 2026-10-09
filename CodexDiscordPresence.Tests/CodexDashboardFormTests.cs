using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace CodexDiscordPresence.Tests;

public sealed class CodexDashboardFormTests
{
    [Fact]
    public void Form_HostsOriginalHtmlInsteadOfRecreatingTheCanvas()
    {
        RunSta(() =>
        {
            using var form = new CodexDashboardForm(new PresenceRuntimeState());
            Assert.Equal(ProductBrand.Name, form.Text);
            Assert.Equal($"{ProductBrand.Name} dashboard", form.AccessibleName);
            Assert.Equal(new Size(402, 379), form.ClientSize);
            Assert.Equal(FormBorderStyle.None, form.FormBorderStyle);
            Assert.IsType<WebView2>(Assert.Single(form.Controls.Cast<Control>()));
        });
    }

    [Theory]
    [InlineData(ProviderIds.Codex)]
    [InlineData(ProviderIds.ClaudeCode)]
    [InlineData(ProviderIds.Antigravity)]
    public void Form_ProviderMessagePersistsOnlyKnownIntegrations(string providerId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dashboard-bridge-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "state.json");
        try
        {
            RunSta(() =>
            {
                var runtime = new PresenceRuntimeState();
                using var form = new CodexDashboardForm(runtime, new PresenceStateStore(), path);
                using var message = JsonDocument.Parse(JsonSerializer.Serialize(new {type="provider", providerId, enabled=true}));
                form.HandleMessage(message.RootElement);
                Assert.True(runtime.IsProviderEnabled(providerId, false));
                Assert.True(new PresenceStateStore().Load(path).IsProviderEnabled(providerId, false));
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("{\"type\":\"provider\",\"providerId\":\"unknown\",\"enabled\":true}")]
    [InlineData("{\"type\":\"provider\",\"providerId\":\"codex\",\"enabled\":\"false\"}")]
    [InlineData("{\"type\":false}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Form_InvalidMessageDoesNotChangeState(string json)
    {
        RunSta(() =>
        {
            var runtime = new PresenceRuntimeState();
            using var form = new CodexDashboardForm(runtime);
            using var message = JsonDocument.Parse(json);
            form.HandleMessage(message.RootElement);
            Assert.Empty(runtime.ProviderEnabled);
        });
    }

    [Fact]
    public void Form_DisposeStopsTimerAndDoesNotInitializeBrowserBeforeShowing()
    {
        RunSta(() =>
        {
            var form = new CodexDashboardForm(new PresenceRuntimeState());
            var browser = Assert.IsType<WebView2>(form.Controls[0]);
            var timer = (System.Windows.Forms.Timer)typeof(CodexDashboardForm).GetField("_refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Assert.Null(browser.CoreWebView2);
            Assert.False(timer.Enabled);
            form.Dispose();
            Assert.False(timer.Enabled);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => {try {action();} catch (Exception error) {failure = error;}});
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
