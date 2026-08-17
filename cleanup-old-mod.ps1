$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataDir = if ($env:VINTAGE_STORY_DATA) { $env:VINTAGE_STORY_DATA } else { Join-Path $env:APPDATA "VintagestoryData" }

$targets = @(
    (Join-Path $projectRoot "bin\\Debug\\Mods\\mod"),
    (Join-Path $projectRoot "bin\\Release\\Mods\\mod"),
    (Join-Path $dataDir "Mods\\mod")
)

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Host "已删除：$target"
    } else {
        Write-Host "不存在：$target"
    }
}

Write-Host "旧 mod 目录清理完成。请在 Visual Studio 中执行 清理解决方案，然后重新生成。"
