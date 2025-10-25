@echo off
setlocal enabledelayedexpansion

rem Base directory containing the OKD files.
set "BASE_DIR=D:\新しいフォルダー (4)\Song"

rem Location of the OKDPlayer executable. Adjust if the EXE lives elsewhere.
set "OKD_PLAYER=%~dp0OKDPlayer.exe"

rem Maximum number of parallel conversions. Leave empty to use the CPU core count.
set "MAX_CONCURRENT="

if not exist "%OKD_PLAYER%" (
    echo Could not find OKDPlayer.exe at "%OKD_PLAYER%".
    echo Please update the OKD_PLAYER variable in this script to point to the correct location.
    exit /b 1
)

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "try {
    $ErrorActionPreference = 'Stop'
    $base = $env:BASE_DIR
    $exe = $env:OKD_PLAYER
    $maxSetting = $env:MAX_CONCURRENT
    if ([string]::IsNullOrWhiteSpace($maxSetting)) {
        $max = [Environment]::ProcessorCount
    } else {
        $parsed = 0
        if (-not [int]::TryParse($maxSetting, [ref]$parsed) -or $parsed -lt 1) {
            Write-Warning \"MAX_CONCURRENT must be a positive integer. Falling back to processor count.\"
            $max = [Environment]::ProcessorCount
        } else {
            $max = $parsed
        }
    }

    if (-not (Test-Path -LiteralPath $exe)) {
        throw \"Could not find OKDPlayer.exe at '$exe'.\"
    }

    if (-not (Test-Path -LiteralPath $base)) {
        throw \"Base directory '$base' does not exist.\"
    }

    $files = Get-ChildItem -LiteralPath $base -Filter '1006.*' -File -Recurse
    if (-not $files) {
        Write-Host 'No OKD files found.'
        exit 0
    }

    Write-Host (\"Found {0} OKD file(s). Using up to {1} concurrent conversion(s).\" -f $files.Count, $max)

    $jobs = @()
    foreach ($file in $files) {
        $inputPath = $file.FullName
        $outputPath = "$($file.FullName).midi"
        $jobs += Start-Job -ArgumentList $exe, $inputPath, $outputPath -ScriptBlock {
            param($exePath, $inPath, $outPath)
            Write-Output \"Converting '$inPath' -> '$outPath'...\"
            & $exePath -i $inPath -o $outPath
            if ($LASTEXITCODE -ne 0) {
                throw \"Conversion failed for '$inPath' with exit code $LASTEXITCODE.\"
            }
            Write-Output \"Finished '$inPath'.\"
        }

        while ((Get-Job -State Running).Count -ge $max) {
            $completed = Wait-Job -Any $jobs
            $jobOutput = Receive-Job -Job $completed -ErrorAction SilentlyContinue
            if ($jobOutput) {
                $jobOutput | ForEach-Object { Write-Host $_ }
            }
            if ($completed.JobStateInfo.State -eq 'Failed') {
                $reason = $completed.JobStateInfo.Reason
                if ($reason) {
                    throw $reason
                } else {
                    throw \"A conversion job failed.\"
                }
            }
            Remove-Job -Job $completed
            $jobs = $jobs | Where-Object { $_.Id -ne $completed.Id }
        }
    }

    foreach ($job in $jobs) {
        Wait-Job -Job $job | Out-Null
        $jobOutput = Receive-Job -Job $job -ErrorAction SilentlyContinue
        if ($jobOutput) {
            $jobOutput | ForEach-Object { Write-Host $_ }
        }
        if ($job.JobStateInfo.State -eq 'Failed') {
            $reason = $job.JobStateInfo.Reason
            if ($reason) {
                throw $reason
            } else {
                throw \"A conversion job failed.\"
            }
        }
        Remove-Job -Job $job
    }

    Write-Host 'Conversion complete.'
} catch {
    Write-Error $_
    exit 1
}"

endlocal
