// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track B scratch: the smallest Avalonia window that shows the kind of content the
// configuration window shows, published NativeAOT to weigh it. It is published, never run.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var list = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
            list.Children.Add(new TextBlock { Text = "BrowserAI", FontSize = 20 });
            list.Children.Add(new TextBlock { Text = "Claude Code: registered   Codex: registered" });
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "Claude Code", Content = new Button { Content = "Unregister" } });
            tabs.Items.Add(new TabItem { Header = "Codex", Content = new Button { Content = "Unregister" } });
            tabs.Items.Add(new TabItem { Header = "Sessions", Content = new DataGridLikeList() });
            list.Children.Add(tabs);
            desktop.MainWindow = new Window { Title = "BrowserAI", Width = 720, Height = 520, Content = list };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class DataGridLikeList : ListBox
{
    public DataGridLikeList()
    {
        ItemsSource = new[] { @"C:\Source\repo  Claude Code  browser open", @"C:\Source\other  Codex  idle" };
        SelectionMode = SelectionMode.Multiple;
    }
}
