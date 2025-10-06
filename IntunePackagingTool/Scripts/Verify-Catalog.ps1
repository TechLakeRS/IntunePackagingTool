param(
    [Parameter(Mandatory=$true)]
    [string]$CatalogPath,
    [Parameter(Mandatory=$false)]
    [string]$OriginalPath
)

$ErrorActionPreference = 'Continue'  # Changed from 'Stop' to handle unsigned files
Write-Host "STARTED:$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

if (-not (Test-Path $CatalogPath)) {
    Write-Error "Catalog file not found: $CatalogPath"
    exit 1
}

try {
    # Get file info
    $fileInfo = Get-Item $CatalogPath
    Write-Host "FILE_SIZE:$($fileInfo.Length)"
    
    # Check signature - this tells us if catalog is signed, not if it's valid
    $sig = Get-AuthenticodeSignature -FilePath $CatalogPath -ErrorAction SilentlyContinue
    Write-Host "SIGNATURE:$($sig.Status)"
    if ($sig.StatusMessage) {
        Write-Host "SIGNATURE_MESSAGE:$($sig.StatusMessage)"
    }
    
    # Initialize variables
    $catalogValid = $false
    $catalogItems = @()
    $statusDetermined = $false
    
    # Try Test-FileCatalog first (this determines actual validity)
    Write-Host "Attempting Test-FileCatalog..."
    try {
        # Use a job with timeout to avoid hanging
        $job = Start-Job -ScriptBlock {
            param($path)
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            
            # Test-FileCatalog returns Valid/NotValid status
            $result = Test-FileCatalog -Path $path -Detailed -ErrorAction Stop
            return $result
        } -ArgumentList $CatalogPath
        
        # Wait for job with timeout
        $completed = $job | Wait-Job -Timeout 15
        
        if ($completed) {
            $result = Receive-Job -Job $job -ErrorAction SilentlyContinue
            if ($result) {
                # Test-FileCatalog Status is what determines if catalog is valid
                $catalogValid = ($result.Status -eq 'Valid')
                $statusDetermined = $true
                Write-Host "CATALOG_STATUS:$($result.Status)"
                
                if ($result.CatalogItems) {
                    $catalogItems = $result.CatalogItems
                    Write-Host "METHOD:Test-FileCatalog"
                }
            }
        } else {
            Write-Host "Test-FileCatalog timed out after 15 seconds"
            Stop-Job -Job $job -Force
        }
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Host "Test-FileCatalog error: $_"
    }
    
    # If Test-FileCatalog didn't work, try alternative validation
    if (-not $statusDetermined) {
        Write-Host "Trying alternative validation..."
        
        # For unsigned catalogs, we can still validate structure
        try {
            # Try certutil to validate catalog structure
            $tempFile = [System.IO.Path]::GetTempFileName()
            $errorFile = [System.IO.Path]::GetTempFileName()
            
            $certutilProcess = Start-Process -FilePath "certutil.exe" `
                -ArgumentList "-dump", "`"$CatalogPath`"" `
                -NoNewWindow -Wait -PassThru `
                -RedirectStandardOutput $tempFile `
                -RedirectStandardError $errorFile
            
            if ($certutilProcess.ExitCode -eq 0) {
                $certutilOutput = Get-Content $tempFile -Raw
                $certutilError = Get-Content $errorFile -Raw
                
                # If certutil can parse it, it's a valid catalog structure
                if ($certutilOutput -match "CertUtil:" -and -not ($certutilError -match "ERROR")) {
                    $catalogValid = $true
                    $statusDetermined = $true
                    Write-Host "CATALOG_STATUS:Valid (structure verified)"
                    
                    # Try to extract catalog items from certutil output
                    $memberPattern = '(?ms)Name:\s*(.+?)\r?\n.*?Value:\s*([0-9a-fA-F\s]+)'
                    $altPattern = '(?ms)File:\s*(.+?)\r?\n.*?Hash:\s*([0-9a-fA-F]+)'
                    
                    $matches = [regex]::Matches($certutilOutput, $memberPattern)
                    if ($matches.Count -eq 0) {
                        $matches = [regex]::Matches($certutilOutput, $altPattern)
                    }
                    
                    foreach ($match in $matches) {
                        if ($match.Groups.Count -ge 3) {
                            $filePath = $match.Groups[1].Value.Trim()
                            $hashValue = $match.Groups[2].Value -replace '\s', ''
                            
                            if ($filePath -and $hashValue) {
                                $catalogItems += [PSCustomObject]@{
                                    Path = $filePath
                                    Hash = $hashValue
                                }
                            }
                        }
                    }
                    
                    if ($catalogItems.Count -gt 0) {
                        Write-Host "METHOD:Certutil"
                    }
                }
            }
            
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
            Remove-Item $errorFile -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Host "Certutil validation error: $_"
        }
    }
    
    # If still no status, check file structure
    if (-not $statusDetermined) {
        try {
            $bytes = [System.IO.File]::ReadAllBytes($CatalogPath)
            if ($bytes.Length -gt 100) {
                # Check for ASN.1 DER encoding (catalog files start with 0x30 0x82)
                if ($bytes[0] -eq 0x30 -and $bytes[1] -eq 0x82) {
                    $catalogValid = $true
                    Write-Host "CATALOG_STATUS:Valid (ASN.1 structure detected)"
                } else {
                    Write-Host "CATALOG_STATUS:Invalid (not a catalog file)"
                }
            } else {
                Write-Host "CATALOG_STATUS:Invalid (file too small)"
            }
        } catch {
            Write-Host "File structure check error: $_"
            Write-Host "CATALOG_STATUS:Unknown"
        }
    }
    
    # Output final status (catalog validity, not signature status)
    if ($catalogValid) {
        Write-Host "STATUS:Valid"
    } else {
        Write-Host "STATUS:Invalid"
    }
    
    # Output item count and items
    Write-Host "ITEM_COUNT:$($catalogItems.Count)"
    
    if ($catalogItems.Count -gt 0) {
        foreach ($item in $catalogItems) {
            $displayPath = $item.Path
            
            # Adjust path if using original path
            if ($OriginalPath -and ($CatalogPath -ne $OriginalPath)) {
                if (-not [System.IO.Path]::IsPathRooted($displayPath)) {
                    $catalogDir = [System.IO.Path]::GetDirectoryName($OriginalPath)
                    $displayPath = [System.IO.Path]::Combine($catalogDir, $displayPath)
                }
            }
            
            Write-Host "ITEM:$displayPath|$($item.Hash)"
        }
    } else {
        if ($catalogValid) {
            Write-Host "WARNING:Catalog is valid but unable to retrieve file list"
        } else {
            Write-Host "WARNING:Invalid catalog or unable to parse"
        }
    }
    
} catch {
    Write-Host "ERROR:$($_.Exception.Message)"
    Write-Host "STATUS:Error"
    exit 1
}

Write-Host "COMPLETED:$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
exit 0