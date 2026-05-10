using DouDouDeDou.Models;

namespace DouDouDeDou.Services;

public sealed record ControllerDetectionInfo(
    string DeviceName,
    ControllerType ControllerType,
    string InputMode,
    bool IsConnected,
    int? XInputIndex,
    int? VendorId = null,
    int? ProductId = null,
    int? UsagePage = null,
    int? Usage = null,
    string DevicePath = "",
    string Hint = "");

public sealed class ControllerDetectionService
{
    private readonly XInputControllerService _xInputControllerService;
    private readonly HidControllerService _hidControllerService;

    public ControllerDetectionService(
        XInputControllerService xInputControllerService,
        HidControllerService hidControllerService)
    {
        _xInputControllerService = xInputControllerService;
        _hidControllerService = hidControllerService;
    }

    public ControllerDetectionInfo DetectBest()
    {
        if (_xInputControllerService.TryGetFirstConnectedIndex(out var xInputIndex)
            && _xInputControllerService.TryGetState(xInputIndex, out var xInputState))
        {
            return new ControllerDetectionInfo(
                xInputState.DeviceName,
                xInputState.ControllerType,
                xInputState.InputMode,
                true,
                xInputState.XInputUserIndex,
                xInputState.VendorId,
                xInputState.ProductId,
                xInputState.UsagePage,
                xInputState.Usage,
                xInputState.DevicePath,
                "XInput 优先");
        }

        var hid = _hidControllerService.DetectFirstHid();
        if (hid is not null)
        {
            return hid;
        }

        return new ControllerDetectionInfo("未检测到手柄", ControllerType.None, "Auto", false, null);
    }
}
