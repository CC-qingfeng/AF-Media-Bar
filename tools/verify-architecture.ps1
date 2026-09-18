param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\src\AFMediaBar')
)

$ErrorActionPreference = 'Stop'
$viewModelRoot = Join-Path $SourceRoot 'ViewModels'
$serviceRoot = Join-Path $SourceRoot 'Classes\Services'
$violations = @()

function Test-XmlDocumentation {
    param(
        [string[]]$Lines,
        [int]$DeclarationIndex
    )

    for ($index = $DeclarationIndex - 1; $index -ge 0; $index--) {
        $trimmed = $Lines[$index].Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('[')) {
            continue
        }

        return $trimmed.StartsWith('///')
    }

    return $false
}

foreach ($file in Get-ChildItem -LiteralPath $viewModelRoot -Filter '*.cs' -Recurse) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($pattern in @('Wpf\.Ui\.Controls', 'AFMediaBar\.Views', 'App\.Services', '\bSettingsWindow\b', '\bNavigationViewItem\b', '\bSymbolIcon\b')) {
        if ($text -match $pattern) {
            $violations += "ViewModel UI dependency: $($file.FullName) matches $pattern"
        }
    }

    if ($text -match '(?m)\bnew\s+[A-Za-z_][A-Za-z0-9_]*Service\b') {
        $violations += "ViewModel manual service construction: $($file.FullName)"
    }
}

# 服务必须按所有权归档，避免新的基础设施继续堆积在扁平的兜底目录中。
# Services are grouped by ownership so new infrastructure does not accumulate in a flat catch-all directory.
foreach ($file in Get-ChildItem -LiteralPath $serviceRoot -Filter '*.cs' -File) {
    $violations += "Service source must be placed in an ownership folder: $($file.FullName)"
}

$publicTypeCount = 0
$sourceFileCount = 0
foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Filter '*.cs' -Recurse |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $sourceFileCount++
    $lines = Get-Content -LiteralPath $file.FullName
    $text = $lines -join "`n"
    # 英文半句是成对模板中的稳定 ASCII 标识，也避免 Windows PowerShell 5 对无 BOM 脚本的编码歧义。
    # The English half is a stable ASCII marker for the paired template and avoids Windows PowerShell 5 encoding ambiguity in BOM-less scripts.
    if ($text -match 'Provides the public .+ entry point required by this component\.') {
        $violations += "Boilerplate XML documentation is not allowed: $($file.FullName)"
    }
    if ($text -match '/// <summary>\s*/// </summary>') {
        $violations += "Empty XML summary is not allowed: $($file.FullName)"
    }

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*public\s+(?:(?:abstract|sealed|static|partial|unsafe|readonly|new)\s+)*(?:class|record|struct|interface|enum|delegate)\s+\w+') {
            $publicTypeCount++
            if (-not (Test-XmlDocumentation -Lines $lines -DeclarationIndex $index)) {
                $violations += "Public type lacks bilingual XML documentation: $($file.FullName):$($index + 1)"
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'ViewModel boundary scan passed.'
Write-Output 'Service ownership-folder scan passed.'
Write-Output "XML documentation quality scan passed ($sourceFileCount source files)."
Write-Output "Public type XML documentation scan passed ($publicTypeCount types)."
