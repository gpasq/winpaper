using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Paperboy;

public sealed class WallpaperStore : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    const string SettingsKey = @"Software\Paperboy";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Paperboy";

    List<BingImage> images = new();
    public List<BingImage> Images { get => images; private set { images = value; Changed(); } }

    /// Archive images by year, loaded on demand.
    public Dictionary<int, List<BingImage>> Archive { get; } = new();

    int? archiveLoadingYear;
    public int? ArchiveLoadingYear { get => archiveLoadingYear; private set { archiveLoadingYear = value; Changed(); } }

    bool isLoading;
    public bool IsLoading { get => isLoading; private set { isLoading = value; Changed(); } }

    string? busyId;
    public string? BusyId { get => busyId; private set { busyId = value; Changed(); } }

    string? errorMessage;
    public string? ErrorMessage { get => errorMessage; private set { errorMessage = value; Changed(); } }

    string? activeId;
    public string? ActiveId
    {
        get => activeId;
        private set
        {
            activeId = value;
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            if (value == null) key.DeleteValue("ActiveID", false);
            else key.SetValue("ActiveID", value);
            Changed();
        }
    }

    bool autoApplyLatest;
    public bool AutoApplyLatest
    {
        get => autoApplyLatest;
        set
        {
            if (autoApplyLatest == value) return;
            autoApplyLatest = value;
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue("AutoApplyLatest", value ? 1 : 0, RegistryValueKind.DWord);
            Changed();
            if (value) _ = RefreshAsync();
        }
    }

    bool launchAtLogin;
    public bool LaunchAtLogin
    {
        get => launchAtLogin;
        set
        {
            if (launchAtLogin == value) return;
            launchAtLogin = value;
            SetLaunchAtLogin(value);
            Changed();
        }
    }

    /// %USERPROFILE%\Pictures\Paperboy
    public string Folder { get; }

    readonly DispatcherTimer timer;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    public WallpaperStore()
    {
        Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Paperboy");
        Directory.CreateDirectory(Folder);

        using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
        {
            activeId = key?.GetValue("ActiveID") as string;
            autoApplyLatest = key?.GetValue("AutoApplyLatest") is int i && i != 0;
        }
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
            launchAtLogin = run?.GetValue(RunValue) != null;
        // Keep the login item pointing at this copy of the app if it has moved.
        if (launchAtLogin) SetLaunchAtLogin(true);

        // Bing rotates once a day; checking hourly is plenty.
        timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public void Start()
    {
        _ = RefreshAsync();
        timer.Start();
        // Re-apply to newly attached displays.
        SystemEvents.DisplaySettingsChanged += (_, _) => dispatcher.BeginInvoke(ReapplyActive);
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            Images = await BingService.FetchRecentAsync();
            ErrorMessage = null;
            if (AutoApplyLatest && Images.Count > 0 && Images[0].Id != ActiveId)
                await ApplyAsync(Images[0]);
        }
        catch (Exception e)
        {
            ErrorMessage = $"Couldn't reach Bing: {e.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadArchiveAsync(int year)
    {
        if (Archive.ContainsKey(year) || ArchiveLoadingYear == year) return;
        ArchiveLoadingYear = year;
        try
        {
            Archive[year] = await ArchiveService.FetchYearAsync(year);
            ErrorMessage = null;
            Changed(nameof(Archive));
        }
        catch (Exception e)
        {
            ErrorMessage = $"Couldn't load the {year} archive: {e.Message}";
        }
        finally
        {
            if (ArchiveLoadingYear == year) ArchiveLoadingYear = null;
        }
    }

    public string LocalPath(BingImage image) => LocalPath(image.Id);

    string LocalPath(string id) => Path.Combine(Folder, $"{id}.jpg");

    public bool IsDownloaded(BingImage image) => File.Exists(LocalPath(image));

    public async Task<string> DownloadAsync(BingImage image)
    {
        var dest = LocalPath(image);
        if (File.Exists(dest)) return dest;
        using var response = await Net.Http.GetAsync(image.FullUrl, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"The server returned {(int)response.StatusCode}.");
        var tmp = dest + ".part";
        await using (var file = File.Create(tmp))
            await response.Content.CopyToAsync(file);
        File.Move(tmp, dest, overwrite: true);
        Changed(nameof(IsDownloaded));
        return dest;
    }

    public async Task ApplyAsync(BingImage image)
    {
        BusyId = image.Id;
        try
        {
            var path = await DownloadAsync(image);
            await Task.Run(() => SetDesktop(path));
            ActiveId = image.Id;
            ErrorMessage = null;
        }
        catch (Exception e)
        {
            ErrorMessage = $"Couldn't set wallpaper: {e.Message}";
        }
        finally
        {
            BusyId = null;
        }
    }

    void ReapplyActive()
    {
        if (ActiveId == null) return;
        var path = LocalPath(ActiveId);
        if (!File.Exists(path)) return;
        Task.Run(() => { try { SetDesktop(path); } catch { } });
    }

    /// Fills every display with the image (the Windows "Fill" fit: scale proportionally, crop overflow).
    static void SetDesktop(string path)
    {
        using (var desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
        {
            desktop.SetValue("WallpaperStyle", "10");
            desktop.SetValue("TileWallpaper", "0");
        }
        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void OpenFolder() =>
        Process.Start(new ProcessStartInfo { FileName = Folder, UseShellExecute = true });

    void SetLaunchAtLogin(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) run.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else run.DeleteValue(RunValue, false);
        }
        catch (Exception e)
        {
            ErrorMessage = $"Launch at login: {e.Message}";
        }
    }

    const uint SPI_SETDESKWALLPAPER = 0x0014;
    const uint SPIF_UPDATEINIFILE = 0x01;
    const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);
}
