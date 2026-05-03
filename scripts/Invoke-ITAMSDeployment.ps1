param(
    [Parameter(Mandatory = $true)]
    [string]$Commit,

    [string]$BareRepositoryPath = "C:\Git\ITAMS.git",
    [string]$DeployRoot = "C:\inetpub\ITAMS",
    [string]$SecretsPath = "C:\ITAMS\Deploy\itams-production-env.json",
    [string]$PublicUrl = "https://itams.app",
    [string]$LiveTestResolveAddress = "127.0.0.1",
    [string]$SiteAppPoolIdentity = "IIS AppPool\ITAMS.Site",
    [string]$ApiAppPoolIdentity = "IIS AppPool\ITAMS.Api",
    [int]$RetainReleases = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-ChildPath {
    param(
        [string]$Parent,
        [string]$Child
    )

    $parentFullPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\')
    $childFullPath = [System.IO.Path]::GetFullPath($Child).TrimEnd('\')
    if (-not $childFullPath.StartsWith($parentFullPath + "\", [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $childFullPath.Equals($parentFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected path. Parent: $parentFullPath Child: $childFullPath"
    }
}

function Remove-DirectoryIfExists {
    param(
        [string]$Parent,
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Assert-ChildPath -Parent $Parent -Child $Path
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Write-Host ">> $FilePath $($Arguments -join ' ')"
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Add-CanonicalRedirectRules {
    param(
        [string]$WebConfigPath,
        [string]$CanonicalHost = "itams.app",
        [string]$WwwHost = "www.itams.app",
        [string]$LegacyIpHost = "20.120.240.89"
    )

    [xml]$config = Get-Content -Raw -LiteralPath $WebConfigPath
    $systemWebServer = $config.SelectSingleNode("/configuration/system.webServer")
    if ($null -eq $systemWebServer) {
        $systemWebServer = $config.CreateElement("system.webServer")
        [void]$config.configuration.AppendChild($systemWebServer)
    }

    $rewrite = $systemWebServer.SelectSingleNode("rewrite")
    if ($null -eq $rewrite) {
        $rewrite = $config.CreateElement("rewrite")
        [void]$systemWebServer.AppendChild($rewrite)
    }

    $rules = $rewrite.SelectSingleNode("rules")
    if ($null -eq $rules) {
        $rules = $config.CreateElement("rules")
        [void]$rewrite.AppendChild($rules)
    }

    foreach ($ruleName in @("Redirect www.itams.app to itams.app", "Redirect public IP to itams.app", "Redirect itams.app HTTP to HTTPS")) {
        $existingRule = $rules.SelectSingleNode("rule[@name='$ruleName']")
        if ($existingRule) {
            [void]$rules.RemoveChild($existingRule)
        }
    }

    function New-RedirectRule {
        param(
            [string]$Name,
            [string]$HostPattern,
            [bool]$HttpsOffOnly = $false
        )

        $rule = $config.CreateElement("rule")
        $rule.SetAttribute("name", $Name)
        $rule.SetAttribute("stopProcessing", "true")

        $match = $config.CreateElement("match")
        $match.SetAttribute("url", "(.*)")
        [void]$rule.AppendChild($match)

        $conditions = $config.CreateElement("conditions")
        $conditions.SetAttribute("logicalGrouping", "MatchAll")

        $hostCondition = $config.CreateElement("add")
        $hostCondition.SetAttribute("input", "{HTTP_HOST}")
        $hostCondition.SetAttribute("pattern", $HostPattern)
        [void]$conditions.AppendChild($hostCondition)

        if ($HttpsOffOnly) {
            $httpsCondition = $config.CreateElement("add")
            $httpsCondition.SetAttribute("input", "{HTTPS}")
            $httpsCondition.SetAttribute("pattern", "off")
            [void]$conditions.AppendChild($httpsCondition)
        }

        [void]$rule.AppendChild($conditions)

        $action = $config.CreateElement("action")
        $action.SetAttribute("type", "Redirect")
        $action.SetAttribute("url", "https://$CanonicalHost/{R:1}")
        $action.SetAttribute("redirectType", "Permanent")
        [void]$rule.AppendChild($action)

        return $rule
    }

    $canonicalPattern = "^$([regex]::Escape($CanonicalHost))(?::\d+)?$"
    $wwwPattern = "^$([regex]::Escape($WwwHost))(?::\d+)?$"
    $legacyIpPattern = "^$([regex]::Escape($LegacyIpHost))(?::\d+)?$"

    foreach ($rule in @(
        (New-RedirectRule -Name "Redirect itams.app HTTP to HTTPS" -HostPattern $canonicalPattern -HttpsOffOnly $true),
        (New-RedirectRule -Name "Redirect public IP to itams.app" -HostPattern $legacyIpPattern),
        (New-RedirectRule -Name "Redirect www.itams.app to itams.app" -HostPattern $wwwPattern)
    )) {
        if ($rules.FirstChild) {
            [void]$rules.InsertBefore($rule, $rules.FirstChild)
        }
        else {
            [void]$rules.AppendChild($rule)
        }
    }

    $config.Save($WebConfigPath)
}

function Add-ApiRewriteExclusion {
    param(
        [string]$WebConfigPath
    )

    [xml]$config = Get-Content -Raw -LiteralPath $WebConfigPath
    $rules = $config.SelectNodes("/configuration/system.webServer/rewrite/rules/rule")
    foreach ($rule in $rules) {
        $action = $rule.SelectSingleNode("action")
        if ($null -eq $action -or $action.GetAttribute("type") -ne "Rewrite") {
            continue
        }

        $conditions = $rule.SelectSingleNode("conditions")
        if ($null -eq $conditions) {
            $conditions = $config.CreateElement("conditions")
            $conditions.SetAttribute("logicalGrouping", "MatchAll")
            [void]$rule.AppendChild($conditions)
        }
        elseif (-not $conditions.HasAttribute("logicalGrouping")) {
            $conditions.SetAttribute("logicalGrouping", "MatchAll")
        }

        $hasApiExclusion = $false
        foreach ($condition in @($conditions.SelectNodes("add"))) {
            if ($condition.GetAttribute("input") -eq "{URL}" -and
                $condition.GetAttribute("pattern") -eq "^/api(?:/|$)" -and
                $condition.GetAttribute("negate") -eq "true") {
                $hasApiExclusion = $true
            }
        }

        if (-not $hasApiExclusion) {
            $apiCondition = $config.CreateElement("add")
            $apiCondition.SetAttribute("input", "{URL}")
            $apiCondition.SetAttribute("pattern", "^/api(?:/|$)")
            $apiCondition.SetAttribute("negate", "true")
            [void]$conditions.PrependChild($apiCondition)
        }
    }

    $config.Save($WebConfigPath)
}

function Add-SecurityHeaders {
    param(
        [string]$WebConfigPath
    )

    [xml]$config = Get-Content -Raw -LiteralPath $WebConfigPath
    $systemWebServer = $config.SelectSingleNode("/configuration/system.webServer")
    if ($null -eq $systemWebServer) {
        $systemWebServer = $config.SelectSingleNode("/configuration/location/system.webServer")
    }

    if ($null -eq $systemWebServer) {
        $systemWebServer = $config.CreateElement("system.webServer")
        [void]$config.configuration.AppendChild($systemWebServer)
    }

    $httpProtocol = $systemWebServer.SelectSingleNode("httpProtocol")
    if ($null -eq $httpProtocol) {
        $httpProtocol = $config.CreateElement("httpProtocol")
        [void]$systemWebServer.AppendChild($httpProtocol)
    }

    $customHeaders = $httpProtocol.SelectSingleNode("customHeaders")
    if ($null -eq $customHeaders) {
        $customHeaders = $config.CreateElement("customHeaders")
        [void]$httpProtocol.AppendChild($customHeaders)
    }

    $headers = @(
        @{ Name = "Strict-Transport-Security"; Value = "max-age=31536000; includeSubDomains" },
        @{ Name = "Content-Security-Policy-Report-Only"; Value = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self' https://itams.app https://www.itams.app; font-src 'self' data:; form-action 'self'; upgrade-insecure-requests" },
        @{ Name = "X-Content-Type-Options"; Value = "nosniff" },
        @{ Name = "X-Frame-Options"; Value = "DENY" },
        @{ Name = "Referrer-Policy"; Value = "strict-origin-when-cross-origin" },
        @{ Name = "Permissions-Policy"; Value = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()" }
    )

    foreach ($header in $headers) {
        foreach ($existingHeader in @($customHeaders.SelectNodes("add[@name='$($header.Name)']"))) {
            [void]$customHeaders.RemoveChild($existingHeader)
        }

        $node = $config.CreateElement("add")
        $node.SetAttribute("name", $header.Name)
        $node.SetAttribute("value", $header.Value)
        [void]$customHeaders.AppendChild($node)
    }

    $config.Save($WebConfigPath)
}

function Set-IisResponseHardening {
    param(
        [string]$WebConfigPath
    )

    [xml]$config = Get-Content -Raw -LiteralPath $WebConfigPath
    $systemWebServer = $config.SelectSingleNode("/configuration/system.webServer")
    if ($null -eq $systemWebServer) {
        $systemWebServer = $config.SelectSingleNode("/configuration/location/system.webServer")
    }

    if ($null -eq $systemWebServer) {
        $systemWebServer = $config.CreateElement("system.webServer")
        [void]$config.configuration.AppendChild($systemWebServer)
    }

    $security = $systemWebServer.SelectSingleNode("security")
    if ($null -eq $security) {
        $security = $config.CreateElement("security")
        [void]$systemWebServer.AppendChild($security)
    }

    $requestFiltering = $security.SelectSingleNode("requestFiltering")
    if ($null -eq $requestFiltering) {
        $requestFiltering = $config.CreateElement("requestFiltering")
        [void]$security.AppendChild($requestFiltering)
    }

    $requestFiltering.SetAttribute("removeServerHeader", "true")
    $config.Save($WebConfigPath)
}

function Set-ApiEnvironmentVariables {
    param(
        [string]$WebConfigPath,
        [string]$SecretsPath
    )

    if (-not (Test-Path -LiteralPath $SecretsPath)) {
        throw "Missing deployment environment file: $SecretsPath"
    }

    $secrets = Get-Content -Raw -LiteralPath $SecretsPath | ConvertFrom-Json
    foreach ($requiredName in @("ASPNETCORE_ENVIRONMENT", "MongoDb__ConnectionString", "Jwt__SigningKey", "Cors__AllowedOrigins__0")) {
        $property = $secrets.PSObject.Properties[$requiredName]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "Missing required deployment environment value: $requiredName"
        }
    }

    if ($null -eq $secrets.PSObject.Properties["AllowedHosts"]) {
        $secrets | Add-Member -NotePropertyName "AllowedHosts" -NotePropertyValue "itams.app;www.itams.app"
    }

    [xml]$config = Get-Content -Raw -LiteralPath $WebConfigPath
    $aspNetCore = $config.SelectSingleNode("/configuration/location/system.webServer/aspNetCore")
    if ($null -eq $aspNetCore) {
        throw "API web.config does not contain an aspNetCore node."
    }

    $environmentVariables = $aspNetCore.SelectSingleNode("environmentVariables")
    if ($null -eq $environmentVariables) {
        $environmentVariables = $config.CreateElement("environmentVariables")
        [void]$aspNetCore.AppendChild($environmentVariables)
    }

    foreach ($property in $secrets.PSObject.Properties) {
        $existing = @($environmentVariables.SelectNodes("environmentVariable")) |
            Where-Object { $_.GetAttribute("name") -eq $property.Name } |
            Select-Object -First 1

        if ($existing) {
            $existing.SetAttribute("value", [string]$property.Value)
        }
        else {
            $node = $config.CreateElement("environmentVariable")
            $node.SetAttribute("name", [string]$property.Name)
            $node.SetAttribute("value", [string]$property.Value)
            [void]$environmentVariables.AppendChild($node)
        }
    }

    $config.Save($WebConfigPath)
}

function Test-ReleaseArtifacts {
    param(
        [string]$ClientPath,
        [string]$ApiPath,
        [string]$ExpectedApiBaseUrl
    )

    $indexPath = Join-Path $ClientPath "wwwroot\index.html"
    $indexHtml = Get-Content -Raw -LiteralPath $indexPath
    if ($indexHtml -match "#\[\.\{fingerprint\}\]") {
        throw "Published client index.html still contains a Blazor fingerprint placeholder."
    }

    if ($indexHtml -notmatch "_framework/blazor\.webassembly\.[^""]+\.js") {
        throw "Published client index.html does not reference the fingerprinted Blazor startup script."
    }

    $clientSettingsPath = Join-Path $ClientPath "wwwroot\appsettings.json"
    $clientSettings = Get-Content -Raw -LiteralPath $clientSettingsPath | ConvertFrom-Json
    if ($clientSettings.ApiBaseUrl -ne $ExpectedApiBaseUrl) {
        throw "Client ApiBaseUrl was '$($clientSettings.ApiBaseUrl)', expected '$ExpectedApiBaseUrl'."
    }

    foreach ($compressedSettingsPath in @("$clientSettingsPath.br", "$clientSettingsPath.gz")) {
        if (Test-Path -LiteralPath $compressedSettingsPath) {
            throw "Stale compressed appsettings file exists: $compressedSettingsPath"
        }
    }

    [xml]$clientConfig = Get-Content -Raw -LiteralPath (Join-Path $ClientPath "web.config")
    $securityHeaders = @($clientConfig.SelectNodes("/configuration/system.webServer/httpProtocol/customHeaders/add"))
    foreach ($requiredHeader in @("Strict-Transport-Security", "Content-Security-Policy-Report-Only", "X-Content-Type-Options", "X-Frame-Options", "Referrer-Policy", "Permissions-Policy")) {
        if (-not ($securityHeaders | Where-Object { $_.GetAttribute("name") -eq $requiredHeader })) {
            throw "Published client web.config is missing security header $requiredHeader."
        }
    }

    $clientRequestFiltering = $clientConfig.SelectSingleNode("/configuration/system.webServer/security/requestFiltering")
    if ($null -eq $clientRequestFiltering -or $clientRequestFiltering.GetAttribute("removeServerHeader") -ne "true") {
        throw "Published client web.config does not suppress the IIS Server header."
    }

    $apiWebConfigPath = Join-Path $ApiPath "web.config"
    [xml]$apiConfig = Get-Content -Raw -LiteralPath $apiWebConfigPath
    $envVars = @($apiConfig.SelectNodes("/configuration/location/system.webServer/aspNetCore/environmentVariables/environmentVariable"))
    foreach ($requiredName in @("ASPNETCORE_ENVIRONMENT", "MongoDb__ConnectionString", "Jwt__SigningKey", "Cors__AllowedOrigins__0", "AllowedHosts")) {
        if (-not ($envVars | Where-Object { $_.GetAttribute("name") -eq $requiredName -and -not [string]::IsNullOrWhiteSpace($_.GetAttribute("value")) })) {
            throw "Published API web.config is missing environment variable $requiredName."
        }
    }

    $apiRequestFiltering = $apiConfig.SelectSingleNode("/configuration/location/system.webServer/security/requestFiltering")
    if ($null -eq $apiRequestFiltering -or $apiRequestFiltering.GetAttribute("removeServerHeader") -ne "true") {
        throw "Published API web.config does not suppress the IIS Server header."
    }
}

function Restart-AppPoolSafe {
    param(
        [string]$Name
    )

    $state = (Get-WebAppPoolState -Name $Name).Value
    if ($state -eq "Started") {
        Restart-WebAppPool -Name $Name
    }
    else {
        Start-WebAppPool -Name $Name
    }
}

function Invoke-LiveRequest {
    param(
        [string]$Url,
        [string]$ResolveAddress
    )

    $uri = [Uri]$Url
    $port = if ($uri.IsDefaultPort) {
        if ($uri.Scheme -eq "https") { 443 } else { 80 }
    }
    else {
        $uri.Port
    }

    $bodyPath = [System.IO.Path]::GetTempFileName()
    try {
        $arguments = @("--silent", "--show-error", "--max-time", "60")
        if (-not [string]::IsNullOrWhiteSpace($ResolveAddress)) {
            $arguments += @("--resolve", "$($uri.Host):${port}:$ResolveAddress")
        }

        $arguments += @("--output", $bodyPath, "--write-out", "%{http_code}", $Url)
        $statusText = & curl.exe @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "curl.exe failed for $Url with exit code $LASTEXITCODE."
        }

        return [pscustomobject]@{
            StatusCode = [int]$statusText
            Content = Get-Content -Raw -LiteralPath $bodyPath
        }
    }
    finally {
        Remove-Item -LiteralPath $bodyPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-LiveSite {
    param(
        [string]$PublicUrl,
        [string]$ExpectedApiBaseUrl,
        [string]$ResolveAddress
    )

    $rootResponse = Invoke-LiveRequest -Url "$PublicUrl/" -ResolveAddress $ResolveAddress
    if ([int]$rootResponse.StatusCode -ne 200 -or $rootResponse.Content -notmatch "blazor\.webassembly") {
        throw "The live site root did not return the expected Blazor HTML."
    }

    $settingsResponse = Invoke-LiveRequest -Url "$PublicUrl/appsettings.json" -ResolveAddress $ResolveAddress
    $settings = $settingsResponse.Content | ConvertFrom-Json
    if ($settings.ApiBaseUrl -ne $ExpectedApiBaseUrl) {
        throw "The live appsettings.json does not point at the expected API URL."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $apiStatusCode = $null
    do {
        try {
            $apiResponse = Invoke-LiveRequest -Url "$PublicUrl/api/auth/me" -ResolveAddress $ResolveAddress
            $apiStatusCode = [int]$apiResponse.StatusCode
        }
        catch {
            $apiStatusCode = $null
        }

        if ($apiStatusCode -eq 401) {
            return
        }

        Start-Sleep -Seconds 3
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Expected /api/auth/me to return 401 from the API, but received $apiStatusCode."
}

function Remove-OldReleases {
    param(
        [string]$ReleasesRoot,
        [int]$RetainReleases
    )

    if ($RetainReleases -lt 1 -or -not (Test-Path -LiteralPath $ReleasesRoot)) {
        return
    }

    $oldReleases = Get-ChildItem -LiteralPath $ReleasesRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $RetainReleases

    foreach ($release in $oldReleases) {
        Assert-ChildPath -Parent $ReleasesRoot -Child $release.FullName
        Remove-Item -LiteralPath $release.FullName -Recurse -Force
    }
}

$shortCommit = $Commit
if ($shortCommit.Length -gt 12) {
    $shortCommit = $shortCommit.Substring(0, 12)
}

$expectedApiBaseUrl = "$PublicUrl/api/"
$workRoot = Join-Path $DeployRoot "_worktrees"
$worktreePath = Join-Path $workRoot $shortCommit
$releasesRoot = Join-Path $DeployRoot "releases"
$releasePath = Join-Path $releasesRoot $shortCommit
$clientPublishPath = Join-Path $releasePath "site"
$apiPublishPath = Join-Path $releasePath "api"

Assert-ChildPath -Parent $DeployRoot -Child $workRoot
Assert-ChildPath -Parent $DeployRoot -Child $releasesRoot
Assert-ChildPath -Parent $workRoot -Child $worktreePath
Assert-ChildPath -Parent $releasesRoot -Child $releasePath

Write-Host "Deploying ITAMS commit $Commit to $releasePath"

New-Item -ItemType Directory -Path $workRoot, $releasesRoot -Force | Out-Null
Remove-DirectoryIfExists -Parent $workRoot -Path $worktreePath
Remove-DirectoryIfExists -Parent $releasesRoot -Path $releasePath
New-Item -ItemType Directory -Path $worktreePath, $clientPublishPath, $apiPublishPath -Force | Out-Null

try {
    Invoke-Native -FilePath "git" -Arguments @("--git-dir=$BareRepositoryPath", "--work-tree=$worktreePath", "checkout", "-f", $Commit, "--", ".") -WorkingDirectory $DeployRoot

    Invoke-Native -FilePath "dotnet" -Arguments @("restore", ".\ITAMS.sln") -WorkingDirectory $worktreePath
    Invoke-Native -FilePath "dotnet" -Arguments @("build", ".\ITAMS.sln", "-c", "Release", "--no-restore") -WorkingDirectory $worktreePath
    Invoke-Native -FilePath "dotnet" -Arguments @("test", ".\ITAMS.Api.Tests\ITAMS.Api.Tests.csproj", "-c", "Release", "--no-build") -WorkingDirectory $worktreePath

    Invoke-Native -FilePath "dotnet" -Arguments @("publish", ".\frontend\ITAMS.Client\ITAMS.Client.csproj", "-c", "Release", "-o", $clientPublishPath) -WorkingDirectory $worktreePath
    Invoke-Native -FilePath "dotnet" -Arguments @("publish", ".\ITAMS.Api\ITAMS.Api.csproj", "-c", "Release", "-o", $apiPublishPath) -WorkingDirectory $worktreePath

    Write-Utf8NoBom -Path (Join-Path $clientPublishPath "wwwroot\appsettings.json") -Content "{`"ApiBaseUrl`":`"$expectedApiBaseUrl`"}"
    Remove-Item -LiteralPath (Join-Path $clientPublishPath "wwwroot\appsettings.json.br") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $clientPublishPath "wwwroot\appsettings.json.gz") -Force -ErrorAction SilentlyContinue

    Add-CanonicalRedirectRules -WebConfigPath (Join-Path $clientPublishPath "web.config")
    Add-ApiRewriteExclusion -WebConfigPath (Join-Path $clientPublishPath "web.config")
    Add-SecurityHeaders -WebConfigPath (Join-Path $clientPublishPath "web.config")
    Set-IisResponseHardening -WebConfigPath (Join-Path $clientPublishPath "web.config")
    Set-ApiEnvironmentVariables -WebConfigPath (Join-Path $apiPublishPath "web.config") -SecretsPath $SecretsPath
    Set-IisResponseHardening -WebConfigPath (Join-Path $apiPublishPath "web.config")
    New-Item -ItemType Directory -Path (Join-Path $apiPublishPath "logs") -Force | Out-Null

    Test-ReleaseArtifacts -ClientPath $clientPublishPath -ApiPath $apiPublishPath -ExpectedApiBaseUrl $expectedApiBaseUrl

    icacls $clientPublishPath /grant "${SiteAppPoolIdentity}:(OI)(CI)RX" /T | Out-Null
    icacls $apiPublishPath /grant "${ApiAppPoolIdentity}:(OI)(CI)RX" /T | Out-Null
    icacls (Join-Path $apiPublishPath "logs") /grant "${ApiAppPoolIdentity}:(OI)(CI)M" /T | Out-Null

    Import-Module WebAdministration
    $previousSitePath = (Get-Website -Name "ITAMS").PhysicalPath
    $previousApiPath = (Get-WebApplication -Site "ITAMS" | Where-Object { $_.Path -eq "/api" }).PhysicalPath

    try {
        Set-ItemProperty "IIS:\Sites\ITAMS" -Name physicalPath -Value $clientPublishPath
        Set-ItemProperty "IIS:\Sites\ITAMS\api" -Name physicalPath -Value $apiPublishPath
        Set-ItemProperty "IIS:\Sites\ITAMS" -Name serverAutoStart -Value $true
        Set-ItemProperty "IIS:\Sites\ITAMS\api" -Name preloadEnabled -Value $true

        Restart-AppPoolSafe -Name "ITAMS.Api"
        Restart-AppPoolSafe -Name "ITAMS.Site"
        Start-Website -Name "ITAMS"

        Test-LiveSite -PublicUrl $PublicUrl -ExpectedApiBaseUrl $expectedApiBaseUrl -ResolveAddress $LiveTestResolveAddress
    }
    catch {
        Write-Warning "Live smoke test failed after switching release. Rolling back IIS paths."
        Set-ItemProperty "IIS:\Sites\ITAMS" -Name physicalPath -Value $previousSitePath
        Set-ItemProperty "IIS:\Sites\ITAMS\api" -Name physicalPath -Value $previousApiPath
        Restart-AppPoolSafe -Name "ITAMS.Api"
        Restart-AppPoolSafe -Name "ITAMS.Site"
        throw
    }

    Write-Utf8NoBom -Path (Join-Path $DeployRoot "current-release.txt") -Content "$Commit`r`n"
    Remove-OldReleases -ReleasesRoot $releasesRoot -RetainReleases $RetainReleases

    Write-Host "ITAMS deployment completed for $Commit"
}
finally {
    Remove-DirectoryIfExists -Parent $workRoot -Path $worktreePath
}
