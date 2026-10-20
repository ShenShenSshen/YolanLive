using YolanLive.WinUI.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System;
using System.Threading.Tasks;
using Windows.UI.Popups;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YolanLive.WinUI.Helper
{
    public static class Utils
    {

        public  static void ShowMessageToast(string message, int seconds = 2)
        {
            App.ActiveWindow?.DispatcherQueue.TryEnqueue(() => new MessageToast(message, TimeSpan.FromSeconds(seconds)).Show());
        }
        public async static Task<bool> ShowDialog(string title, string content)
        {
            MessageDialog messageDialog = new MessageDialog(content, title);
            messageDialog.Commands.Add(new UICommand() { Label = "确定", Id = true });
            messageDialog.Commands.Add(new UICommand() { Label = "取消", Id = false });
            InitializeWithWindow(messageDialog);
            var result = await messageDialog.ShowAsync();
            return (bool)result.Id;
        }
        public static bool SetClipboard(string content)
        {
            try
            {
                Windows.ApplicationModel.DataTransfer.DataPackage pack = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pack.SetText(content);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pack);
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

     

        public static void InitializeWithWindow(object value) => WinRT.Interop.InitializeWithWindow.Initialize(value, App.ActiveWindow.Handle);
        public static WriteableBitmap CreateQrCode(string text)
        {
            var writer = new ZXing.BarcodeWriterPixelData { Format = ZXing.BarcodeFormat.QR_CODE,
                Options = new ZXing.Common.EncodingOptions { Width = 260, Height = 260, Margin = 4 } };
            var pixels = writer.Write(text);
            var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
            using var stream = bitmap.PixelBuffer.AsStream();
            stream.Write(pixels.Pixels, 0, pixels.Pixels.Length);
            return bitmap;
        }

    }

}
