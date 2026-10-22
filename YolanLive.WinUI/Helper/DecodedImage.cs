using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
namespace YolanLive.WinUI.Helper;
public static class DecodedImage
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached("Url", typeof(string), typeof(DecodedImage), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty HasPictureProperty = DependencyProperty.RegisterAttached("HasPicture", typeof(bool), typeof(DecodedImage), new PropertyMetadata(false));
    public static bool GetHasPicture(DependencyObject target) => (bool)target.GetValue(HasPictureProperty);
    public static void SetHasPicture(DependencyObject target, bool value) => target.SetValue(HasPictureProperty, value);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(DecodedImage), new PropertyMetadata(null));
    public static string GetUrl(DependencyObject target) => (string)target.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject target, string value) => target.SetValue(UrlProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        var state = (State)target.GetValue(StateProperty);
        if (state == null) { state = new State(element); target.SetValue(StateProperty, state); }
        state.Update();
    }
    private sealed class State
    {
        private readonly FrameworkElement element;
        private XamlRoot root;
        private BitmapImage bitmap;
        private string url;
        private int pixels;
        private bool loaded;
        private bool updateQueued;
        internal State(FrameworkElement element)
        {
            this.element = element;
            loaded = element.IsLoaded;
            if (element is Image target)
            {
                target.ImageOpened += (_, _) =>
                {
                    if (bitmap == null || !ReferenceEquals(target.Source, bitmap)) return;
                    SetHasPicture(element, true);
                    InteractionMotion.Reveal(element, 160);
                };
                target.ImageFailed += (_, _) => { if (ReferenceEquals(target.Source, bitmap)) SetHasPicture(element, false); };
            }
            element.Loaded += (_, _) =>
            {
                loaded = true;
                Update();
                // Run once after initial layout, even if no further size/root event arrives.
                QueueUpdate();
            };
            element.Unloaded += (_, _) =>
            {
                loaded = false;
                InteractionMotion.Reset(element);
                if (root != null) root.Changed -= RootChanged;
                root = null;
                SetSource(null);
                ClearBitmap(); pixels = 0; url = null;
            };
            element.SizeChanged += (_, _) => QueueUpdate();
        }
        private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs e) => QueueUpdate();
        private void QueueUpdate()
        {
            if (updateQueued || (!loaded && !element.IsLoaded)) return;
            updateQueued = true;
            if (!element.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                updateQueued = false;
                Update();
            })) updateQueued = false;
        }
        private void SetSource(BitmapImage image)
        { if (element is Image target) target.Source = image; else if (element is PersonPicture avatar) avatar.ProfilePicture = image; }
        private void ClearBitmap()
        {
            if (bitmap != null) { bitmap.ImageOpened -= BitmapOpened; bitmap.ImageFailed -= BitmapFailed; }
            bitmap = null;
            SetHasPicture(element, false);
        }
        private void BitmapOpened(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, bitmap)) SetHasPicture(element, true);
        }
        private void BitmapFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (ReferenceEquals(sender, bitmap)) SetHasPicture(element, false);
        }
        internal void Update()
        {
            if (!loaded && !element.IsLoaded) return;
            if (root != element.XamlRoot)
            {
                if (root != null) root.Changed -= RootChanged;
                root = element.XamlRoot;
                if (root != null) root.Changed += RootChanged;
            }
            var value = GetUrl(element)?.Trim();
            if (value?.StartsWith("//", StringComparison.Ordinal) == true) value = "https:" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                SetSource(null);
                ClearBitmap(); pixels = 0; url = null;
                return;
            }
            var width = Math.Ceiling(element.ActualWidth * (root?.RasterizationScale ?? 1));
            // A zero-width initial layout must not prevent loading. Let WinUI size
            // the first decode automatically, then apply the measured size after layout.
            var desired = width > 0 ? (int)Math.Clamp(Math.Ceiling(width / 32) * 32, 32, 4096) : 0;
            if (url == value && bitmap != null)
            {
                if (desired == 0 || (pixels >= desired && desired >= pixels * 0.75)) return;
                bitmap.DecodePixelWidth = desired;
                pixels = desired;
                return;
            }
            ClearBitmap();
            bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Physical, DecodePixelWidth = desired };
            bitmap.ImageOpened += BitmapOpened;
            bitmap.ImageFailed += BitmapFailed;
            pixels = desired; url = value;
            // Attach to the live tree before assigning UriSource, as recommended by WinUI.
            SetSource(bitmap);
            // Missing-picture UI describes absence/failure, not loading progress.
            // Native completion events may be deferred for cached or re-decoded images.
            SetHasPicture(element, true);
            bitmap.UriSource = uri;
        }
    }
}
