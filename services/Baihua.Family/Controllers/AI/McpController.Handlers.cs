using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Mcp;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class McpController : ControllerBase
{
    private IActionResult HandleInitialize(McpJsonRpcRequest request)
    {
        McpInitializeRequest initRequest;
        try
        {
            initRequest = request.Params.HasValue
                ? JsonSerializer.Deserialize<McpInitializeRequest>(request.Params.Value.GetRawText())
                  ?? new McpInitializeRequest()
                : new McpInitializeRequest();
        }
        catch
        {
            initRequest = new McpInitializeRequest();
        }

        var result = _mcpService.Initialize(initRequest);
        return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
    }

    private IActionResult HandleListTools(McpJsonRpcRequest request)
    {
        var result = _mcpService.ListTools();
        return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
    }

    private async Task<IActionResult> HandleCallToolAsync(McpJsonRpcRequest request)
    {
        McpToolCallRequest callRequest;
        try
        {
            callRequest = request.Params.HasValue
                ? JsonSerializer.Deserialize<McpToolCallRequest>(request.Params.Value.GetRawText())
                  ?? new McpToolCallRequest()
                : new McpToolCallRequest();
        }
        catch (Exception ex)
        {
            return BadRpcResponse(request.Id, -32602, $"Invalid params: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(callRequest.Name))
            return BadRpcResponse(request.Id, -32602, "Missing tool name");

        var result = await _mcpService.CallToolAsync(callRequest, HttpContext.RequestAborted);
        return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
    }

    private IActionResult HandleListPrompts(McpJsonRpcRequest request)
    {
        var result = _mcpService.ListPrompts();
        return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
    }

    private IActionResult HandleGetPrompt(McpJsonRpcRequest request)
    {
        McpPromptGetRequest promptRequest;
        try
        {
            promptRequest = request.Params.HasValue
                ? JsonSerializer.Deserialize<McpPromptGetRequest>(request.Params.Value.GetRawText())
                  ?? new McpPromptGetRequest()
                : new McpPromptGetRequest();
        }
        catch (Exception ex)
        {
            return BadRpcResponse(request.Id, -32602, $"Invalid params: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(promptRequest.Name))
            return BadRpcResponse(request.Id, -32602, "Missing prompt name");

        try
        {
            var result = _mcpService.GetPrompt(promptRequest);
            return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
        }
        catch (Exception ex)
        {
            return BadRpcResponse(request.Id, -32602, ex.Message);
        }
    }

    private IActionResult HandleListResources(McpJsonRpcRequest request)
    {
        var result = _mcpService.ListResources();
        return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
    }

    private IActionResult HandleReadResource(McpJsonRpcRequest request)
    {
        McpResourceReadRequest readRequest;
        try
        {
            readRequest = request.Params.HasValue
                ? JsonSerializer.Deserialize<McpResourceReadRequest>(request.Params.Value.GetRawText())
                  ?? new McpResourceReadRequest()
                : new McpResourceReadRequest();
        }
        catch (Exception ex)
        {
            return BadRpcResponse(request.Id, -32602, $"Invalid params: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(readRequest.Uri))
            return BadRpcResponse(request.Id, -32602, "Missing resource URI");

        try
        {
            var result = _mcpService.ReadResource(readRequest);
            return Ok(new McpJsonRpcResponse { Id = request.Id, Result = result });
        }
        catch (Exception ex)
        {
            return BadRpcResponse(request.Id, -32602, ex.Message);
        }
    }

    private static IActionResult BadRpcResponse(object? id, int code, string message)
    {
        return new ObjectResult(new McpJsonRpcResponse
        {
            Id = id,
            Error = new McpJsonRpcError { Code = code, Message = message }
        })
        { StatusCode = 200 };
    }
}
