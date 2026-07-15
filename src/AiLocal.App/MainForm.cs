using System.Runtime.InteropServices;
using AiLocal.Node.Hosting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AiLocal.App;

public sealed class MainForm : Form
{
    private readonly RunningNodeApp _node;
    private readonly WebView2 _webView;

    public MainForm(RunningNodeApp node)
    {
        _node = node;
        Text = "AiLocal";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1200, 760);
        // Match the dashboard's dark theme so the window never flashes white
        // around/behind the WebView while it loads.
        BackColor = Color.FromArgb(14, 14, 14);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(14, 14, 14)
        };
        Controls.Add(_webView);

        Load += OnLoad;
    }

    /// <summary>Borderless-with-native-behaviors, the Hermes/Claude Code
    /// look: strip WS_CAPTION (no OS titlebar) but KEEP the sizing frame and
    /// min/max styles, so resize edges, Win+arrow snapping and animations
    /// still work. The dashboard's own topbar becomes the caption - dragging
    /// is handled by WebView2's non-client-region support (CSS app-region:
    /// drag, enabled in OnLoad), and min/max/close are HTML buttons that
    /// post messages back to this form (OnWebMessageReceived).</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_CAPTION = 0x00C00000;
            var cp = base.CreateParams;
            cp.Style &= ~WS_CAPTION;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
    }

    /// <summary>Dark DWM frame - still relevant without a caption, since the
    /// remaining sizing border and Win11's rounded-corner frame pick up the
    /// dark accent instead of flashing light. Attribute 20 is
    /// DWMWA_USE_IMMERSIVE_DARK_MODE on 20H1+, 19 on older builds; both
    /// probes are best-effort.</summary>
    private void ApplyDarkTitleBar()
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            try
            {
                // Lets the page's `app-region: drag` regions act as the real
                // window caption (drag to move, double-click to maximize,
                // right-click for the system menu). Available in recent
                // WebView2 runtimes; without it the window still works, it
                // just can't be dragged by the topbar - so a failure here is
                // deliberately non-fatal.
                _webView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
            }
            catch { /* older WebView2 runtime - window chrome still usable */ }
            _webView.Source = new Uri(_node.LocalEndpoint);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Microsoft Edge WebView2 Runtime is required to run AiLocal as a desktop app.",
                "AiLocal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AiLocal failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch { return; }

        switch (message)
        {
            case "win:minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "win:maximize":
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                break;
            case "win:close":
                Close();
                break;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
            _webView.CoreWebView2.Navigate(e.Uri);
    }
}
