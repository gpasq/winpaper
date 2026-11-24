using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Paperboy;

/// Light/dark brushes that follow the Windows app theme and accent color.
static class Theme
{
    public static bool IsDark { get; private set; }

    public static void Apply(ResourceDictionary r)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            IsDark = key?.GetValue("AppsUseLightTheme") is int light && light == 0;

        var accent = AccentColor();
        if (IsDark)
        {
            Set(r, "BgBrush", 0x2B2B2B);
            Set(r, "MenuBgBrush", 0x2C2C2C);
            Set(r, "FgBrush", 0xFFFFFF);
            Set(r, "Fg2Brush", 0xFFFFFF, 0.72);
            Set(r, "Fg3Brush", 0xFFFFFF, 0.46);
            Set(r, "PlaceholderBrush", 0xFFFFFF, 0.08);
            Set(r, "LineBrush", 0xFFFFFF, 0.09);
            Set(r, "ControlBrush", 0xFFFFFF, 0.06);
            Set(r, "HoverBrush", 0xFFFFFF, 0.08);
            Set(r, "ErrorBrush", 0xFF99A4);
            r["AccentBrush"] = Freeze(new SolidColorBrush(Lighten(accent, 0.35)));
            r["OnAccentBrush"] = Freeze(new SolidColorBrush(Colors.Black));
        }
        else
        {
            Set(r, "BgBrush", 0xF9F9F9);
            Set(r, "MenuBgBrush", 0xFCFCFC);
            Set(r, "FgBrush", 0x000000, 0.9);
            Set(r, "Fg2Brush", 0x000000, 0.62);
            Set(r, "Fg3Brush", 0x000000, 0.42);
            Set(r, "PlaceholderBrush", 0x000000, 0.07);
            Set(r, "LineBrush", 0x000000, 0.1);
            Set(r, "ControlBrush", 0xFFFFFF, 0.85);
            Set(r, "HoverBrush", 0x000000, 0.05);
            Set(r, "ErrorBrush", 0xC42B1C);
            r["AccentBrush"] = Freeze(new SolidColorBrush(accent));
            r["OnAccentBrush"] = Freeze(new SolidColorBrush(Colors.White));
        }
    }

    static void Set(ResourceDictionary r, string key, uint rgb, double alpha = 1) =>
        r[key] = Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255),
            (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)));

    static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    static Color AccentColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is int abgr)
            return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
}

/// Small indeterminate progress ring.
public sealed class Spinner : Grid
{
    public Spinner()
    {
        Width = Height = 16;
        var ring = new Ellipse
        {
            StrokeThickness = 2,
            StrokeDashCap = PenLineCap.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { 7, 100 },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        ring.SetResourceReference(Shape.StrokeProperty, "Fg2Brush");
        var rotate = new RotateTransform();
        ring.RenderTransform = rotate;
        Children.Add(ring);
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
