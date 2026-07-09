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

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.White
        };
        Controls.Add(_webView);

        Load += OnLoad;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
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

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
            _webView.CoreWebView2.Navigate(e.Uri);
    }
}
