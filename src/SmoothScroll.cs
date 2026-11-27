using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Paperboy;

/// Pixel-precise, eased wheel scrolling for a ScrollViewer.
///
/// WPF's ScrollViewer moves a fixed three lines for every wheel message and ignores how far the wheel
/// actually turned. A precision touchpad sends many small deltas per swipe, so each tiny finger movement
/// jumped a full step. This scrolls in proportion to the delta instead, and eases toward the target
/// so mouse notches glide rather than snap.
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(Animator), typeof(SmoothScroll));

    static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
        {
            var a = new Animator(sv);
            sv.SetValue(StateProperty, a);
            sv.PreviewMouseWheel += a.OnWheel;
        }
        else if (sv.GetValue(StateProperty) is Animator a)
        {
            a.Stop();
            sv.PreviewMouseWheel -= a.OnWheel;
            sv.ClearValue(StateProperty);
        }
    }

    /// Cancels any glide in progress, e.g. before jumping the view programmatically.
    public static void Stop(ScrollViewer sv) => (sv.GetValue(StateProperty) as Animator)?.Stop();

    sealed class Animator
    {
        /// Matches the system feel: one 120-unit notch scrolls WheelScrollLines × 16 px (48 px by default).
        const double PixelsPerLine = 16;
        /// Easing time constants (seconds). Touchpad input is already continuous, so it follows closely.
        const double MouseTau = 0.075, TouchpadTau = 0.03;

        readonly ScrollViewer sv;
        double current, target, tau = MouseTau;
        TimeSpan lastFrame;
        bool running;

        public Animator(ScrollViewer sv) => this.sv = sv;

        public void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (sv.ScrollableHeight <= 0) return;
            e.Handled = true;

            if (!running) current = target = sv.VerticalOffset;

            var lines = SystemParameters.WheelScrollLines;
            var perNotch = lines < 0 ? sv.ViewportHeight : lines * PixelsPerLine;
            target = Math.Clamp(target - e.Delta / 120.0 * perNotch, 0, sv.ScrollableHeight);
            tau = e.Delta % 120 == 0 ? MouseTau : TouchpadTau;

            if (!running)
            {
                running = true;
                lastFrame = TimeSpan.Zero;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        void OnFrame(object? sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            // Rendering can fire more than once per frame with the same timestamp.
            if (now == lastFrame) return;
            var dt = lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - lastFrame).TotalSeconds, 0.1);
            lastFrame = now;

            target = Math.Clamp(target, 0, sv.ScrollableHeight);
            current += (target - current) * (1 - Math.Exp(-dt / tau));
            if (Math.Abs(target - current) < 0.5) current = target;
            sv.ScrollToVerticalOffset(current);
            if (current == target) Stop();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }
}
