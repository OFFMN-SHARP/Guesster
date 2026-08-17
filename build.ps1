#!/usr/bin/env pwsh
# Guesster 编译脚本 - Windows (优化版)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Guesster 编译脚本启动..." -ForegroundColor Cyan

# 清理旧文件
Write-Host "🧹 清理旧构建..." -ForegroundColor Yellow
dotnet clean -c Release
Remove-Item -Recurse -Force ./publish -ErrorAction SilentlyContinue

# 还原 NuGet 包
Write-Host "📦 还原 NuGet 包..." -ForegroundColor Yellow
dotnet restore

# 发布为独立单文件 (Self-contained + Single-file)
Write-Host "🔨 编译独立单文件 (Windows)..." -ForegroundColor Yellow
dotnet publish -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:StripSymbols=true `
    -p:InvariantGlobalization=true `
    -p:UseSystemResourceKeys=true `
    -p:IlcOptimizationPreference=Speed `
    -o ./publish/win-x64

# 压缩
Write-Host "📦 压缩发布文件..." -ForegroundColor Yellow
Compress-Archive -Path ./publish/win-x64/* -DestinationPath ./Guesster-win-x64.zip -Force
Write-Host "✅ 编译完成！" -ForegroundColor Green
Write-Host "📂 输出位置: ./publish/win-x64/Guesster.exe" -ForegroundColor Green
Write-Host "📦 压缩包: ./Guesster-win-x64.zip" -ForegroundColor Green