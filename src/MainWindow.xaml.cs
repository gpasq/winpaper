using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Paperboy;

/// One cell in the thumbnail grid.
public sealed class ThumbItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public BingImage Image { get; }
    public ThumbItem(BingImage image) => Image = image;

    bool isSelected, isActive;
    public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } } }
    public bool IsActive { get => isActive; set { if (isActive != value) { isActive = value; PropertyChanged?.Invoke(this, new(nameof(IsActive))); } } }
}

/// The tray popup: the same view as Paperboy's menu bar window.
public partial class MainWindow : Window
{
    enum Source { Recent, Archive }

    readonly WallpaperStore store;
    Source source = Source.Recent;
    int year = DateTime.Now.Year;
    /// 1–12, or null for the whole year.
    int? month;
    BingImage? selection;

    readonly int[] years = Enumerable.Range(ArchiveService.FirstYear, DateTime.Now.Year - ArchiveService.FirstYear + 1)
                                     .Reverse().ToArray();
    readonly Random random = new();

    List<ThumbItem> items = new();
    object? shownKey;
    DateTime lastHidden;

    public MainWindow(WallpaperStore store)
    {
        this.store = store;
        InitializeComponent();
        store.PropertyChanged += (_, _) => Render();
        Deactivated += (_, _) => HidePopup();
        SizeChanged += (_, _) => { if (IsVisible) Position(); };
        SourceInitialized += (_, _) => RoundCorners();
        Render();
    }

    // MARK: - Popup behavior

    public void Toggle()
    {
        if (IsVisible) HidePopup();
        // A click on the tray icon deactivates (and hides) the popup just before this runs.
        else if ((DateTime.Now - lastHidden).TotalMilliseconds > 300) ShowPopup();
    }

    public void ShowPopup()
    {
        if (!IsVisible)
        {
            Left = Top = -32000;
            Render();
            Show();
        }
        UpdateLayout();
        Position();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetForegroundWindow(hwnd);
    }

    void HidePopup()
    {
        if (!IsVisible) return;
        lastHidden = DateTime.Now;
        Hide();
    }

    /// Anchors the popup in the corner of the work area next to the taskbar, like Windows' own flyouts.
    void Position()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        // Twice, in case moving to a monitor with a different scale resized the window.
        for (var pass = 0; pass < 2; pass++)
        {
            GetWindowRect(hwnd, out var r);
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            var margin = (int)Math.Round(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            var x = work.Left > bounds.Left ? work.Left + margin : work.Right - w - margin;
            var y = work.Top > bounds.Top ? work.Top + margin : work.Bottom - h - margin;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) HidePopup();
        else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control) ((App)Application.Current).Quit();
    }

    // MARK: - State

    List<BingImage> Shown()
    {
        if (source == Source.Recent) return store.Images;
        if (!store.Archive.TryGetValue(year, out var all)) return new();
        if (month is not int m) return all;
        var prefix = $"{year:D4}{m:D2}";
        return all.Where(i => i.StartDate.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    /// The tapped image if it's still in the grid, otherwise the newest one shown.
    BingImage? Current(List<BingImage> shown)
    {
        if (selection != null && shown.Any(i => i.Id == selection.Id)) return selection;
        return shown.FirstOrDefault();
    }

    void Render()
    {
        var shown = Shown();
        var current = Current(shown);

        // Header
        RefreshSpinner.Visibility = store.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = store.IsLoading ? Visibility.Collapsed : Visibility.Visible;

        // Archive controls
        ArchiveControls.Visibility = source == Source.Archive ? Visibility.Visible : Visibility.Collapsed;
        YearLabel.Text = year.ToString(CultureInfo.InvariantCulture);
        MonthLabel.Text = month is int mm ? MonthName(mm) : "All months";

        // Preview
        Preview.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
        if (current != null)
        {
            if (!Equals(RemoteImage.GetUrl(PreviewImage), current.PreviewUrl))
                RemoteImage.SetUrl(PreviewImage, current.PreviewUrl);
            PreviewTitle.Text = current.Title;
            PreviewCaption.Text = current.Caption;
            PreviewCaption.Visibility = current.Caption.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            PreviewDate.Text = current.Date?.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "";
            BusySpinner.Visibility = store.BusyId == current.Id ? Visibility.Visible : Visibility.Collapsed;
            DownloadButton.IsEnabled = !store.IsDownloaded(current);
            ApplyButton.Content = store.ActiveId == current.Id ? "Active" : "Set as Wallpaper";
            ApplyButton.IsEnabled = store.BusyId == null;
        }

        // Grid: rebuild only when the list itself changes; otherwise just update the badges.
        var key = (source, year, month, source == Source.Recent ? store.Images : store.Archive.GetValueOrDefault(year));
        if (!Equals(key, shownKey))
        {
            shownKey = key;
            items = shown.Select(i => new ThumbItem(i)).ToList();
            Thumbs.ItemsSource = items.Chunk(3).ToList();
            ScrollToTop();
        }
        foreach (var item in items)
        {
            item.IsSelected = item.Image.Id == current?.Id;
            item.IsActive = item.Image.Id == store.ActiveId;
        }
        var loadingYear = source == Source.Archive && store.ArchiveLoadingYear == year;
        GridSpinner.Visibility = loadingYear ? Visibility.Visible : Visibility.Collapsed;
        GridEmpty.Visibility = !loadingYear && source == Source.Archive && store.Archive.ContainsKey(year) && shown.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Error + toggles
        ErrorText.Text = store.ErrorMessage ?? "";
        ErrorText.Visibility = store.ErrorMessage != null ? Visibility.Visible : Visibility.Collapsed;
        AutoApplyCheck.IsChecked = store.AutoApplyLatest;
        LaunchCheck.IsChecked = store.LaunchAtLogin;
    }

    void ScrollToTop()
    {
        if (Thumbs.Template?.FindName("Scroller", Thumbs) is ScrollViewer sv) { SmoothScroll.Stop(sv); sv.ScrollToTop(); }
    }

    static string MonthName(int m) => CultureInfo.CurrentCulture.DateTimeFormat.MonthNames[m - 1];

    void LoadArchiveIfNeeded()
    {
        if (source != Source.Archive) return;
        _ = store.LoadArchiveAsync(year);
    }

    /// Jump to a random day anywhere in the archive.
    async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        var pick = years[random.Next(years.Length)];
        await store.LoadArchiveAsync(pick);
        if (!store.Archive.TryGetValue(pick, out var all) || all.Count == 0) return;
        var image = all[random.Next(all.Count)];
        year = pick;
        month = int.Parse(image.StartDate.Substring(4, 2), CultureInfo.InvariantCulture);
        selection = image;
        Render();
    }

    // MARK: - Events

    void Source_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || ArchiveTab == null) return;
        source = ArchiveTab.IsChecked == true ? Source.Archive : Source.Recent;
        Render();
        LoadArchiveIfNeeded();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await store.RefreshAsync();

    void Year_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var y in years)
        {
            var item = new MenuItem { Header = y.ToString(CultureInfo.InvariantCulture), IsChecked = y == year };
            item.Click += (_, _) =>
            {
                year = y;
                Render();
                LoadArchiveIfNeeded();
            };
            menu.Items.Add(item);
        }
        Open(menu, YearButton);
    }

    void Month_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var all = new MenuItem { Header = "All months", IsChecked = month == null };
        all.Click += (_, _) => { month = null; Render(); };
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        for (var m = 1; m <= 12; m++)
        {
            var value = m;
            var item = new MenuItem { Header = MonthName(m), IsChecked = month == m };
            item.Click += (_, _) => { month = value; Render(); };
            menu.Items.Add(item);
        }
        Open(menu, MonthButton);
    }

    static void Open(ContextMenu menu, Button target)
    {
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.VerticalOffset = 2;
        menu.IsOpen = true;
    }

    void Thumb_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ThumbItem item)
        {
            selection = item.Image;
            Render();
        }
    }

    async void Download_Click(object sender, RoutedEventArgs e)
    {
        var current = Current(Shown());
        if (current == null) return;
        try { await store.DownloadAsync(current); } catch { }
        Render();
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var current = Current(Shown());
        if (current != null) await store.ApplyAsync(current);
    }

    void AutoApply_Click(object sender, RoutedEventArgs e) => store.AutoApplyLatest = AutoApplyCheck.IsChecked == true;

    void Launch_Click(object sender, RoutedEventArgs e) => store.LaunchAtLogin = LaunchCheck.IsChecked == true;

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        HidePopup();
        store.OpenFolder();
    }

    void Quit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();

    // MARK: - Win32

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
