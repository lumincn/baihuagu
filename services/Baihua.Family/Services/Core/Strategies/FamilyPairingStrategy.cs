using TaskRunner.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Pairing;

namespace TaskRunner.Services.Strategies;

/// <summary>
/// 家庭版配对策略：提交配对请求，等待 WebUI 审批
/// </summary>
public class FamilyPairingStrategy : IPairingStrategy
{
    private readonly DeviceService _deviceService;

    public FamilyPairingStrategy(DeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public ActionResult<PairResponse> Pair(string deviceName, string? ipAddress, string? pairCode)
    {
        if (string.IsNullOrEmpty(pairCode))
        {
            return new BadRequestObjectResult(new { error = "配对码不能为空" });
        }

        var pairRequest = _deviceService.SubmitPairRequest(deviceName, pairCode, ipAddress);
        return new OkObjectResult(new PairResponse
        {
            RequestId = pairRequest.RequestId,
            Status = "pending",
            Message = "配对请求已提交，请在 WebUI 中授权"
        });
    }
}
