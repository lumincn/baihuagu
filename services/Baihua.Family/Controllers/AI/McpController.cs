using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Mcp;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

/// <summary>
/// MCP (Model Context Protocol) Server 端点
/// 暴露标准 JSON-RPC 接口，供 Claude/Cursor/VS Code 等客户端连接
/// </summary>
[ApiController]
[Route("mcp")]
public partial class McpController : ControllerBase
{
    private readonly McpServerService _mcpService;
    private readonly ILogger<McpController> _logger;

    public McpController(McpServerService mcpService, ILogger<McpController> logger)
    {
        _mcpService = mcpService;
        _logger = logger;
    }

    /// <summary>
    /// MCP JSON-RPC 主端点（POST /mcp）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleMcpRequest([FromBody] McpJsonRpcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Method))
            return BadRpcResponse(request.Id, -32600, "Missing method");

        _logger.LogDebug("MCP 请求: method={Method}, id={Id}", request.Method, request.Id);

        try
        {
            switch (request.Method)
            {
                case "initialize":
                    return HandleInitialize(request);
                case "notifications/initialized":
                    _logger.LogInformation("MCP 客户端初始化完成");
                    return NoContent();
                case "tools/list":
                    return HandleListTools(request);
                case "tools/call":
                    return await HandleCallToolAsync(request);
                case "prompts/list":
                    return HandleListPrompts(request);
                case "prompts/get":
                    return HandleGetPrompt(request);
                case "resources/list":
                    return HandleListResources(request);
                case "resources/read":
                    return HandleReadResource(request);
                default:
                    return BadRpcResponse(request.Id, -32601, $"Method not found: {request.Method}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 请求处理失败: {Method}", request.Method);
            return BadRpcResponse(request.Id, -32603, $"Internal error: {ex.Message}");
        }
    }

    /// <summary>
    /// MCP SSE (Server-Sent Events) 端点
    /// </summary>
    [HttpGet]
    public IActionResult HandleMcpSse()
    {
        return Ok(new
        {
            protocol = "mcp",
            version = "2024-11-05",
            endpoints = new { jsonrpc = "/mcp", health = "/health" },
            message = "TaskRunner MCP Server is running. Use POST /mcp with JSON-RPC 2.0 payloads."
        });
    }
}
