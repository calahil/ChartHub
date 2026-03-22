using Android.App;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace ChartHub.Services;

public sealed class AndroidQrCodeScannerService : IQrCodeScannerService
{
    private readonly IGmsBarcodeScanner _scanner;

    public AndroidQrCodeScannerService()
    {
        GmsBarcodeScannerOptions scannerOptions = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode)
            .EnableAutoZoom()
            .Build();

        _scanner = GmsBarcodeScanning.GetClient(Application.Context, scannerOptions);
    }

    public bool IsSupported => true;

    public async Task<string?> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Android.Gms.Tasks.Task scanTask = _scanner.StartScan();
            Barcode scannedBarcode = await scanTask.AsAsync<Barcode>().WaitAsync(cancellationToken);
            string? payload = scannedBarcode.RawValue;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                return payload;
            }

            return scannedBarcode.DisplayValue;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == 16)
        {
            // The user dismissed the scanner UI before completing a scan.
            return null;
        }
    }
}
