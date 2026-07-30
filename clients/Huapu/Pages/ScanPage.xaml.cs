using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace MobileApp.Maui.Pages;

public partial class ScanPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    public ScanPage()
    {
        InitializeComponent();

        // 显式设置扫描选项：只识别二维码（2D），提高识别率
        CameraBarcodeReaderView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            TryHarder = true,
            Multiple = false,
        };
    }

    public Task<string?> WaitForResultAsync() => _tcs.Task;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CameraBarcodeReaderView.IsDetecting = true;
    }

    protected override void OnDisappearing()
    {
        CameraBarcodeReaderView.IsDetecting = false;
        CameraBarcodeReaderView.IsTorchOn = false;
        base.OnDisappearing();
    }

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var first = e.Results?.FirstOrDefault();
        if (first == null || string.IsNullOrEmpty(first.Value))
            return;

        // 只取第一个识别结果；关闭弹窗由 MauiQrScanner 统一负责，避免重复 PopModal 竞态
        _tcs.TrySetResult(first.Value);
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        // 关闭弹窗由 MauiQrScanner 统一负责
        _tcs.TrySetResult(null);
    }

    protected override bool OnBackButtonPressed()
    {
        // 关闭弹窗由 MauiQrScanner 统一负责
        _tcs.TrySetResult(null);
        return true;
    }
}
