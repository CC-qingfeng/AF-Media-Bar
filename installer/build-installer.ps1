# AF Media Bar 安装包构建脚本 / AF Media Bar installer build script
#
# 本地与 CI 共用同一条链路：读取项目版本 -> 单文件自包含发布 -> Inno Setup 编译 -> 校验产物与哈希。
# The local and CI paths are the same chain: read the project version, publish the self-contained single
# file, compile with Inno Setup, then verify the artifact and its hash.
#
# 版本号只有一个来源（AFMediaBar.csproj 的 <Version>），因此安装包不可能与程序集版本不一致。
# The version has exactly one source (the <Version> property in AFMediaBar.csproj), so the installer can
# never disagree with the assembly version.

[CmdletBinding()]
param(
    # 覆盖版本号；默认从项目文件读取。 / Overrides the version; read from the project file by default.
    [string]$Version,

    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',

    # 单文件发布目录，与 AFMediaBar.iss 的 PublishDir 对应。 / Single-file publish directory, matching PublishDir in AFMediaBar.iss.
    [string]$PublishDir = 'artifacts\publish',

    # 安装包输出目录，与 AFMediaBar.iss 的 OutputDir 对应。 / Installer output directory, matching OutputDir in AFMediaBar.iss.
    [string]$OutputDir = 'artifacts',

    # Inno Setup 编译器路径；留空时按 PATH、Program Files 顺序查找。 / Inno Setup compiler path; searched in PATH and Program Files when omitted.
    [string]$IsccPath,

    # 复用已存在的发布目录，不再调用 dotnet publish。 / Reuses an existing publish directory instead of running dotnet publish.
    [switch]$SkipPublish,

    # 找不到编译器时尝试用 Chocolatey 安装 Inno Setup（CI 使用）。 / Tries installing Inno Setup through Chocolatey when the compiler is missing (CI).
    [switch]$EnsureIscc,

    # 跳过 restore（要求调用方已经用同一个 RID restore 过）。 / Skips restore (the caller must already have restored for the same RID).
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\AFMediaBar\AFMediaBar.csproj'
$solutionPath = Join-Path $repoRoot 'src\AFMediaBar.slnx'
$issPath = Join-Path $PSScriptRoot 'AFMediaBar.iss'
$publishPath = Join-Path $repoRoot $PublishDir
$outputPath = Join-Path $repoRoot $OutputDir
$exeName = 'AFMediaBar.exe'

function Invoke-DotNet {
    param([string[]]$Arguments)

    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Resolve-Version {
    if ($Version) {
        return $Version
    }

    # -getProperty 需要 .NET 8 及以上的 SDK；仓库的 global.json 已固定在受支持的版本带上。
    # -getProperty requires SDK 8 or newer; the repository's global.json already pins a supported band.
    $resolved = (& dotnet msbuild $projectPath -getProperty:Version -nologo)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the project version through dotnet msbuild.'
    }

    $value = @($resolved | Where-Object { $_ -and $_.ToString().Trim() })[-1].ToString().Trim()
    if (-not $value) {
        throw 'The project file does not declare a <Version> property.'
    }

    return $value
}

function Resolve-Iscc {
    if ($IsccPath) {
        if (-not (Test-Path -LiteralPath $IsccPath)) {
            throw "ISCC.exe was not found at '$IsccPath'."
        }
        return (Resolve-Path -LiteralPath $IsccPath).Path
    }

    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    # 只在变量存在时参与拼接：32 位 Windows 上 ProgramFiles(x86) 未定义。
    # Only joined when the variable exists: ProgramFiles(x86) is undefined on 32-bit Windows.
    $candidates = @()
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if ($root) {
            $candidates += (Join-Path $root 'Inno Setup 6\ISCC.exe')
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    if ($EnsureIscc -and (Get-Command 'choco.exe' -ErrorAction SilentlyContinue)) {
        Write-Host '> choco install innosetup -y --no-progress'
        & choco install innosetup -y --no-progress
        if ($LASTEXITCODE -ne 0) {
            throw "Chocolatey could not install Inno Setup (exit code $LASTEXITCODE)."
        }
        return Resolve-Iscc
    }

    throw @'
Inno Setup 6 was not found. Install it once with either:
  winget install --id JRSoftware.InnoSetup -e
  choco install innosetup -y
then rerun this script (or pass -IsccPath <path to ISCC.exe>, or -EnsureIscc on a runner with Chocolatey).
'@
}

$appVersion = Resolve-Version
Write-Host "AF Media Bar version: $appVersion"

# 先找编译器再发布：缺工具的失败应当是即时的，而不是等完整发布跑完才报错。
# The compiler is located before publishing so a missing tool fails immediately instead of after a full publish.
$iscc = Resolve-Iscc

if (-not $SkipPublish) {
    if (-not $NoRestore) {
        # restore 与 publish 必须使用同一个 RID，否则单文件发布会在 NETSDK1047 上失败。
        # restore and publish must use the same RID, otherwise single-file publish fails with NETSDK1047.
        Invoke-DotNet @('restore', $solutionPath, '-r', $RuntimeIdentifier)
    }

    New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
    Invoke-DotNet @(
        'publish', $projectPath,
        '-c', $Configuration,
        '-r', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        '-p:BuildInParallel=false',
        '-o', $publishPath
    )
}

# 发布模式一旦退化（例如丢失 PublishSingleFile）就会在安装包里塞进整个运行时，这里提前失败。
# A regressed publish mode (a lost PublishSingleFile, for example) would stuff the whole runtime into the
# installer, so the build fails here instead.
if (-not (Test-Path -LiteralPath $publishPath)) {
    throw "Publish directory '$publishPath' does not exist. Run without -SkipPublish first."
}
$publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne $exeName) {
    throw "Expected exactly one file named $exeName in '$publishPath', found: $($publishedFiles.Name -join ', ')."
}

Write-Host "Inno Setup compiler: $iscc"

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Write-Host "> ISCC.exe /DMyAppVersion=$appVersion /DPublishDir=$publishPath"
& $iscc "/DMyAppVersion=$appVersion" "/DPublishDir=$publishPath" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$installerName = "AFMediaBar-Setup-v$appVersion-$RuntimeIdentifier.exe"
$installerPath = Join-Path $outputPath $installerName
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "ISCC reported success but '$installerPath' is missing."
}

$installer = Get-Item -LiteralPath $installerPath
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $outputPath 'installer-sha256.txt'
"$hash  $installerName" | Set-Content -LiteralPath $checksumPath -Encoding ascii

$sizeMb = [Math]::Round($installer.Length / 1MB, 1)
Write-Host ''
Write-Host "Installer: $installerPath"
Write-Host "Size:      $sizeMb MB"
Write-Host "SHA-256:   $hash"
Write-Host "Checksum:  $checksumPath"
Write-Host ''
Write-Host 'Write the size and SHA-256 above into docs\latest.json packages[] when publishing this version.'
