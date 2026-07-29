using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace TaskRunner.Vault.Controllers;

    public class ReindexRequest
    {
        public string VaultId { get; set; } = "";
    }
