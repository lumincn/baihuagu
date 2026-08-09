#requires -Version 5.1
<#
  baihua - Windows + k8s CLI
  Cell of the matrix: OS=windows, deployment=k8s
  Builds images, loads them into the local kind cluster and drives kubectl.

  Usage: .\bh-win-k8s.ps1 <command> [args]
    build               docker build 5 images (docker/ prebuilt context)
    load                load images into kind cluster (kind CLI or ctr import)
    deploy              kubectl apply k8s/ manifests + wait ready
    up                  load + deploy
    status              pods / svc / pvc overview
    logs <svc> [n]      tail pod logs (default 50)
    destroy             delete namespace baihua
    dashboard           open browser to http://localhost:30080
    help                this help
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Arg1 = '',
    [Parameter(Position = 2)]
    [string]$Arg2 = ''
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSCommandPath
$K8sDir = Join-Path $Root 'k8s'
$DockerDir = Join-Path $Root 'docker'
$Namespace = 'baihua'

function Get-Kubectl {
    $k = Get-Command kubectl -ErrorAction SilentlyContinue
    if ($k) { return $k.Source }
    $fallback = 'C:\Program Files\Docker\Docker\resources\bin\kubectl.exe'
    if (Test-Path $fallback) { return $fallback }
    throw 'kubectl not found (install kubectl or Docker Desktop)'
}
$Kubectl = Get-Kubectl

function Get-KindNode {
    $node = docker ps --format '{{.Names}}' | Where-Object { $_ -match 'control-plane' } | Select-Object -First 1
    if (-not $node) { throw 'kind control-plane container not found (is the cluster up?)' }
    return $node
}

function Invoke-Build {
    $images = @(
        @{ Name = 'bh-vault:latest';  Dockerfile = 'Dockerfile.vault.prebuilt';           Context = $DockerDir },
        @{ Name = 'bh-ai:latest';     Dockerfile = 'Dockerfile.taskrunner.ai.prebuilt';   Context = $DockerDir },
        @{ Name = 'bh-webui:latest';  Dockerfile = 'Dockerfile.webui.prebuilt';           Context = $DockerDir },
        @{ Name = 'bh-family:latest'; Dockerfile = 'Dockerfile.family.prebuilt';          Context = $DockerDir },
        @{ Name = 'bh-openvino:latest'; Dockerfile = 'Dockerfile.openvino-server.prebuilt'; Context = $Root }
    )
    foreach ($img in $images) {
        Write-Host "[build] $($img.Name) ..."
        & docker build -f (Join-Path $DockerDir $img.Dockerfile) -t $img.Name $img.Context 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker build failed: $($img.Name)" }
    }
    Write-Host '[build] 5 images done'
}

function Invoke-Load {
    $kindCli = Get-Command kind -ErrorAction SilentlyContinue
    $node = Get-KindNode
    if ($kindCli) {
        foreach ($img in @('bh-vault:latest','bh-ai:latest','bh-webui:latest','bh-family:latest','bh-openvino:latest')) {
            & kind load docker-image $img 2>&1 | Out-Null
            Write-Host "[load] $img"
        }
    } else {
        foreach ($img in @('bh-vault:latest','bh-ai:latest','bh-webui:latest','bh-family:latest','bh-openvino:latest')) {
            Write-Host "[load] $img (ctr import via $node)"
            docker save $img | docker exec -i $node ctr --namespace k8s.io images import - 2>&1 | Out-Null
        }
    }
    Write-Host '[load] done'
}

function Invoke-Deploy {
    $manifests = @('00-namespace.yaml','01-configmap.yaml','02-secret.yaml','03-pvc.yaml','10-intel-gpu-plugin.yaml',
        '20-vault.yaml','21-ai.yaml','22a-openvino.yaml','22-family.yaml','23-webui.yaml','24-nginx-configmap.yaml','25-nginx.yaml')
    foreach ($m in $manifests) {
        Write-Host "[deploy] $m"
        & $Kubectl apply -f (Join-Path $K8sDir $m) 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "kubectl apply failed: $m" }
    }
    Write-Host '[deploy] waiting for pods ...'
    & $Kubectl -n $Namespace wait --for=condition=ready pod -l app.kubernetes.io/part-of=baihua --timeout=300s 2>&1 | ForEach-Object { Write-Host $_ }
    Show-Status
}

function Show-Status {
    Write-Host '=== pods ==='
    & $Kubectl -n $Namespace get pods -o wide
    Write-Host ''
    Write-Host '=== svc ==='
    & $Kubectl -n $Namespace get svc
    Write-Host ''
    Write-Host '=== pvc ==='
    & $Kubectl -n $Namespace get pvc
    Write-Host ''
    Write-Host "entry: http://localhost:30080"
}

function Show-Logs($svc, $n) {
    & $Kubectl -n $Namespace logs -l app=$svc --tail=$n --all-containers=true
}

switch ($Command.ToLower()) {
    'build'     { Invoke-Build }
    'load'      { Invoke-Load }
    'deploy'    { Invoke-Deploy }
    'up'        { Invoke-Load; Invoke-Deploy }
    'status'    { Show-Status }
    'logs'      { $count = 50; if ($Arg2) { $count = [int]$Arg2 }; Show-Logs $Arg1 $count }
    'destroy'   { & $Kubectl delete namespace $Namespace; Write-Host '[destroy] done' }
    'dashboard' { Start-Process 'http://localhost:30080' }
    'help'      { Get-Content $PSCommandPath | Where-Object { $_ -match '^\s{4}[a-z]' } | ForEach-Object { $_.Trim() } }
    default     { Get-Content $PSCommandPath | Where-Object { $_ -match '^\s{4}[a-z]' } | ForEach-Object { $_.Trim() } }
}
