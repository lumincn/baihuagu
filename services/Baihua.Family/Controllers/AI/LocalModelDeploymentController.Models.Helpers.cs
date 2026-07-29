using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class LocalModelDeploymentController
{
    private static string GetPlatformDefaultDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".ollama", "models");
    }
}
