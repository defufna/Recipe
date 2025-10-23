function Sort-Distance {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string]$InputObject
    )

    begin { $items = @() }
    process {
        if ($_ -match '.*="(.*)"') {
            $items += [pscustomobject]@{ T = $_; K = [float]$matches[1] }
        }
    }
    end {
        $items | Sort-Object -Property K | ForEach-Object { $_.T }
    }
}

function Sum-Distance {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [string]$InputObject
    )

    begin { $sum = 0.0 }
    process {
        if ($_ -match '.*="(.*)"') {
            $sum += [float]$matches[1]
        }
    }
    end {
        return $sum
    }
}

function Compare {
    param(
        [string[]]$File1,
        [string[]]$File2
    )

    $f1 = Get-Content $File1 | Sort-Distance
    $f2 = Get-Content $File2 | Sort-Distance

    # Print sums
    $sum1 = ($f1 | Sum-Distance)
    $sum2 = ($f2 | Sum-Distance)
    Write-Host "Sum $($File1): $sum1"
    Write-Host "Sum $($File2): $sum2"

    if ($f1.Length -ne $f2.Length) {
        Write-Host "Files have different number of lines: $($f1.Length) vs $($f2.Length)"
        return $false
    }

    0..$f1.Length | ForEach-Object {
        Write-Output ("{0,-40} {1}" -f $f1[$_], $f2[$_])
    }
}
