using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net;
using Baihua.Core.Localization;

namespace Baihua.Family.Filters
{
    public class GlobalExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<GlobalExceptionFilter> _logger;
        private readonly IStringLocalizer<SharedResources> _loc;

        public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IStringLocalizer<SharedResources> loc)
        {
            _logger = logger;
            _loc = loc;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;
            var requestPath = context.HttpContext.Request.Path;
            var requestMethod = context.HttpContext.Request.Method;

            // 记录结构化日志
            _logger.LogError(
                exception,
                "Unhandled exception occurred at {Method} {Path}: {ExceptionMessage}",
                requestMethod,
                requestPath,
                exception.Message
            );

            // 返回统一的错误响应（不泄露内部异常信息）
            context.Result = new ObjectResult(new
            {
                Success = false,
                Message = _loc["Error_InternalServerError"],
                RequestId = context.HttpContext.TraceIdentifier
            })
            {
                StatusCode = (int)HttpStatusCode.InternalServerError
            };

            context.ExceptionHandled = true;
        }
    }
}