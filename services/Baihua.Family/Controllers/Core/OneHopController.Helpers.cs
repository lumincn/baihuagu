using TaskRunner.Core.Shared;
using TaskRunner.Core.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class OneHopController
{
        #region 工具方法

        private string ExtractIpFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "127.0.0.1";
            
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                // 尝试从URL中提取IP地址
                var match = System.Text.RegularExpressions.Regex.Match(url, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
                return match.Success ? match.Value : "127.0.0.1";
            }
        }

        private int ExtractPortFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return 8788;
            
            try
            {
                var uri = new Uri(url);
                return uri.Port;
            }
            catch
            {
                return 8788;
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
                return "127.0.0.1";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get local IP address");
                return "127.0.0.1";
            }
        }

    #endregion
}
