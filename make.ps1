# Raindrop NuGet Package Build Script (PowerShell)
# Usage:
#   .\make.ps1 build           pack all src projects to local NuGet repo
#   .\make.ps1 push-server     push local packages to remote NuGet server
#   .\make.ps1 publish         build + push-server (one-shot)
#
# Environment variables (required for push-server):
#   NUGET_SERVER_URL    your NuGet server URL
#   NUGET_API_KEY       your NuGet API key
#
# Example:
#   $env:NUGET_SERVER_URL="http://your-server:8080/v3/index.json"
#   $env:NUGET_API_KEY="your-api-key"
#   .\make.ps1 push-server

param(
    [Parameter(Position = 0)]
    [ValidateSet("build", "push-server", "publish", "clean", "list-local")]
    [string]$Command = "build"
)

$ErrorActionPreference = "Stop"

$Version       = "0.0.1"
$LocalRepo     = Join-Path $PSScriptRoot "nuget-local"
$NugetSource   = "raindrop-local"
$RemoteSource  = if ($env:NUGET_SERVER_URL) { $env:NUGET_SERVER_URL } else { $null }
$RemoteApiKey  = if ($env:NUGET_API_KEY)    { $env:NUGET_API_KEY    } else { $null }
$Solution      = Join-Path $PSScriptRoot "Raindrop.slnx"

function Build-Packages {
    Write-Host "=== 编译并打包 v$Version 到本地仓库 ===" -ForegroundColor Cyan

    if (-not (Test-Path $LocalRepo)) {
        New-Item -ItemType Directory -Path $LocalRepo | Out-Null
    }

    dotnet pack $Solution --nologo -v q -c Release `
        -p:Version=$Version `
        -p:GeneratePackageOnBuild=false `
        -o $LocalRepo

    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }

    Write-Host ""
    Write-Host "=== 完成：已生成以下包 ===" -ForegroundColor Green
    Get-ChildItem "$LocalRepo\*.nupkg" | ForEach-Object {
        Write-Host "  $($_.Name) ($($_.Length) bytes)"
    }

    Write-Host ""
    Write-Host "=== 注册本地 NuGet 源（如未注册） ===" -ForegroundColor Cyan
    $sources = dotnet nuget list source | Out-String
    if ($sources -notmatch $NugetSource) {
        dotnet nuget add source $LocalRepo --name $NugetSource
        Write-Host "  已注册: $NugetSource -> $LocalRepo"
    }
    else {
        Write-Host "  已存在: $NugetSource"
    }

    Write-Host ""
    Write-Host "✓ build 完成。本地仓库: $LocalRepo" -ForegroundColor Green
    Write-Host "  其他项目可通过 NuGet 源 '$NugetSource' 引用此仓库。"
}

function Push-ServerPackages {
    if (-not $RemoteSource -or -not $RemoteApiKey) {
        Write-Host "=== 错误：未配置远程 NuGet 服务器 ===" -ForegroundColor Red
        Write-Host ""
        Write-Host "  请先设置环境变量:"
        Write-Host '    $env:NUGET_SERVER_URL="http://your-server:8080/v3/index.json"'
        Write-Host '    $env:NUGET_API_KEY="your-api-key"'
        Write-Host ""
        exit 1
    }

    Write-Host "=== 推送本地包到远程服务器 ===" -ForegroundColor Cyan

    $packages = Get-ChildItem "$LocalRepo\*.nupkg" -ErrorAction SilentlyContinue
    if (-not $packages) {
        Write-Host "!!! 本地仓库无包文件，请先执行 build" -ForegroundColor Red
        exit 1
    }

    Write-Host "  本地仓库: $LocalRepo"
    Write-Host "  远程仓库: $RemoteSource"
    Write-Host ""

    foreach ($pkg in $packages) {
        Write-Host "  推送: $($pkg.Name)" -ForegroundColor Yellow
        dotnet nuget push $pkg.FullName --source $RemoteSource --api-key $RemoteApiKey --skip-duplicate
        if ($LASTEXITCODE -ne 0) { throw "推送失败: $($pkg.Name)" }
    }

    Write-Host ""
    Write-Host "✓ push-server 完成。" -ForegroundColor Green
}

function Clean-LocalRepo {
    Write-Host "=== 清理本地 NuGet 仓库 ===" -ForegroundColor Cyan
    if (Test-Path $LocalRepo) {
        Remove-Item -Recurse -Force $LocalRepo
        Write-Host "✓ 已删除 $LocalRepo"
    }
    else {
        Write-Host "  无需清理（目录不存在）"
    }
}

function List-LocalPackages {
    Write-Host "=== 本地仓库: $LocalRepo ===" -ForegroundColor Cyan
    $packages = Get-ChildItem "$LocalRepo\*.nupkg" -ErrorAction SilentlyContinue
    if ($packages) {
        $packages | ForEach-Object { Write-Host "  $($_.Name)" }
    }
    else {
        Write-Host "  (无包文件)"
    }
}

switch ($Command) {
    "build"        { Build-Packages }
    "push-server"  { Push-ServerPackages }
    "publish"      { Build-Packages; Push-ServerPackages }
    "clean"        { Clean-LocalRepo }
    "list-local"   { List-LocalPackages }
}
