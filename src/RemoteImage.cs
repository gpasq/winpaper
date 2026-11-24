using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Paperboy;

/// Attached property that fills a Border's background with a web image (aspect-fill),
/// showing the placeholder brush until it loads. Decoded images are kept in a small LRU cache.
public static class RemoteImage
{
    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached(
        "DecodeWidth", typeof(int), typeof(RemoteImage), new PropertyMetadata(0));
    public static int GetDecodeWidth(DependencyObject d) => (int)d.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject d, int v) => d.SetValue(DecodeWidthProperty, v);

    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(Uri), typeof(RemoteImage), new PropertyMetadata(null, OnUrlChanged));
    public static Uri? GetUrl(DependencyObject d) => (Uri?)d.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject d, Uri? v) => d.SetValue(UrlProperty, v);

    const int CacheLimit = 300;
    static readonly Dictionary<string, LinkedListNode<(string Key, ImageBrush Brush)>> cache = new();
    static readonly LinkedList<(string Key, ImageBrush Brush)> lru = new();
    static readonly Dictionary<string, Task<ImageBrush?>> inflight = new();
    static readonly SemaphoreSlim gate = new(6);

    static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        var url = e.NewValue as Uri;
        var key = $"{url}|{GetDecodeWidth(border)}";
        if (url != null && cache.TryGetValue(key, out var node))
        {
            lru.Remove(node);
            lru.AddFirst(node);
            border.Background = node.Value.Brush;
            return;
        }
        border.SetResourceReference(Border.BackgroundProperty, "PlaceholderBrush");
        if (url != null) _ = LoadAsync(border, url, key);
    }

    static async Task LoadAsync(Border border, Uri url, string key)
    {
        if (!inflight.TryGetValue(key, out var task))
        {
            task = FetchAsync(url, GetDecodeWidth(border));
            inflight[key] = task;
        }
        var brush = await task;
        inflight.Remove(key);
        if (brush == null) return;
        if (!cache.ContainsKey(key))
        {
            cache[key] = lru.AddFirst((key, brush));
            while (lru.Count > CacheLimit)
            {
                cache.Remove(lru.Last!.Value.Key);
                lru.RemoveLast();
            }
        }
        if (Equals(GetUrl(border), url)) border.Background = brush;
    }

    static async Task<ImageBrush?> FetchAsync(Uri url, int decodeWidth)
    {
        await gate.WaitAsync();
        try
        {
            var bytes = await Net.Http.GetByteArrayAsync(url);
            return await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                return brush;
            });
        }
        catch
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }
}
