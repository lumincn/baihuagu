using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Baihua.Vault.Controllers;

    public class ReindexRequest
    {
        public string VaultId { get; set; } = "";
    }
