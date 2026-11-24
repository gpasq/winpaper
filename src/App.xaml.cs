using System;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Paperboy;

public partial class App : Application
{
    const string InstanceName = "Paperboy.SingleInstance";
    const string ShowEventName = "Paperboy.ShowPopup";

    Mutex? mutex;
    EventWaitHandle? showEvent;
    Forms.NotifyIcon? tray;
    MainWindow? popup;
    WallpaperStore? store;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one copy in the tray; launching it again opens the existing popup.
        mutex = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var other)) other.Set();
            Shutdown();
            return;
        }
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (showEvent.WaitOne()) Dispatcher.BeginInvoke(() => popup?.ShowPopup());
        }) { IsBackground = true }.Start();

        Theme.Apply(Resources);
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                Dispatcher.BeginInvoke(() => Theme.Apply(Resources));
        };

        store = new WallpaperStore();
        popup = new MainWindow(store);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Paperboy", null, (_, _) => popup.ShowPopup());
        menu.Items.Add("Refresh", null, async (_, _) => await store.RefreshAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Paperboy", null, (_, _) => Quit());

        tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Paperboy",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) popup.Toggle();
        };

        store.Start();
    }

    static System.Drawing.Icon LoadTrayIcon()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Resources/AppIcon.ico"));
        using var stream = info!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Quit()
    {
        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        mutex?.Dispose();
        base.OnExit(e);
    }
}
