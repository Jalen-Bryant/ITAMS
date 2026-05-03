param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName "System.IO.Compression.FileSystem"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $repoRoot "docs\ITAMS-Setup-Runbook.docx"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function Escape-Xml {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ($null -eq $Value)
    {
        return ""
    }

    return [System.Security.SecurityElement]::Escape($Value)
}

function New-ParagraphXml {
    param(
        [AllowNull()]
        [string]$Text,
        [string]$Style = "Normal",
        [int]$NumberId = 0,
        [int]$Level = 0
    )

    $paragraphProperties = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($Style))
    {
        $paragraphProperties.Add("<w:pStyle w:val=""$Style""/>")
    }

    if ($NumberId -gt 0)
    {
        $paragraphProperties.Add("<w:numPr><w:ilvl w:val=""$Level""/><w:numId w:val=""$NumberId""/></w:numPr>")
    }

    $paragraphPropertiesXml = if ($paragraphProperties.Count -gt 0)
    {
        $joinedParagraphProperties = $paragraphProperties -join ""
        "<w:pPr>$joinedParagraphProperties</w:pPr>"
    }
    else
    {
        ""
    }

    if ([string]::IsNullOrEmpty($Text))
    {
        return "<w:p>$paragraphPropertiesXml</w:p>"
    }

    $escapedText = Escape-Xml -Value $Text
    return "<w:p>$paragraphPropertiesXml<w:r><w:t xml:space=""preserve"">$escapedText</w:t></w:r></w:p>"
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent))
    {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

$generatedOnLocal = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$generatedOnUtc = (Get-Date).ToUniversalTime().ToString("s") + "Z"

$paragraphs = New-Object System.Collections.Generic.List[string]

$paragraphs.Add((New-ParagraphXml -Text "ITAMS Cross-Machine Setup Runbook" -Style "Title"))
$paragraphs.Add((New-ParagraphXml -Text "Generated on $generatedOnLocal" -Style "Subtitle"))

$paragraphs.Add((New-ParagraphXml -Text "Overview" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "This runbook explains how to restore, configure, and run the current ITAMS solution on another Windows computer. The solution contains an ASP.NET Core API, a Blazor WebAssembly frontend, and an API test project." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "All commands in this guide assume Windows PowerShell and that the current working directory is the repository root." -Style "Normal"))

$paragraphs.Add((New-ParagraphXml -Text "Software and Access Dependencies" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text ".NET 10 SDK" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Internet access for NuGet package restore" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Access to a MongoDB Atlas cluster or another valid MongoDB connection string" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "A modern browser for the Blazor WebAssembly frontend" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Optional development tools such as Visual Studio 2022 or Visual Studio Code" -NumberId 1))

$paragraphs.Add((New-ParagraphXml -Text "Repository Structure" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "ITAMS.Api - ASP.NET Core Web API with MongoDB-backed data access, JWT authentication, and Swagger" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "ITAMS.Api.Tests - automated API and integration tests" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "frontend\ITAMS.Client - Blazor WebAssembly frontend application" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "scripts - repository utility scripts, including this runbook generator" -NumberId 1))

$paragraphs.Add((New-ParagraphXml -Text "Required Configuration" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "The API uses .NET user secrets for machine-specific values. Do not place real secrets in source-controlled files." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "Run the following commands from the repository root to configure a new machine:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "MongoDb:ConnectionString" "<MONGODB_CONNECTION_STRING>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "Jwt:SigningKey" "<JWT_SIGNING_KEY_AT_LEAST_32_CHARACTERS>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Username" "<BOOTSTRAP_ADMIN_USERNAME>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:DisplayName" "<BOOTSTRAP_ADMIN_DISPLAY_NAME>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Email" "<BOOTSTRAP_ADMIN_EMAIL>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Department" "<BOOTSTRAP_ADMIN_DEPARTMENT>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text 'dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Password" "<BOOTSTRAP_ADMIN_PASSWORD>"' -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Important notes:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "Jwt:SigningKey must be at least 32 characters long." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "MongoDb:ConnectionString can point to MongoDB Atlas or another reachable MongoDB instance." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Bootstrap admin seeding only runs when the configured database has no login-capable users." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "If the database already contains users with passwords, sign in with an existing valid account instead of expecting new bootstrap credentials to be created." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "MongoDB Atlas network access rules, cluster availability, and database user credentials must allow connections from the new machine." -NumberId 1))

$paragraphs.Add((New-ParagraphXml -Text "Setup and Startup Steps" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "Trust the local HTTPS development certificate if the browser or runtime reports certificate trust issues:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "dotnet dev-certs https --trust" -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Restore the solution:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "dotnet restore .\ITAMS.sln" -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Build the solution:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "dotnet build .\ITAMS.sln -c Debug" -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Start the API in terminal 1:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "dotnet watch --project .\ITAMS.Api\ITAMS.Api.csproj run --launch-profile https" -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Start the frontend in terminal 2:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "dotnet watch --project .\frontend\ITAMS.Client\ITAMS.Client.csproj run --launch-profile https" -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Once both processes are running, browse to the following URLs:" -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "API Swagger: https://localhost:7004/swagger" -Style "CodeBlock"))
$paragraphs.Add((New-ParagraphXml -Text "Frontend: https://localhost:5173" -Style "CodeBlock"))

$paragraphs.Add((New-ParagraphXml -Text "Validation and Smoke Test" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "Open https://localhost:7004/swagger and confirm the Swagger UI loads." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Open https://localhost:5173 and confirm the Blazor login page loads." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Sign in with an existing valid user or with the configured bootstrap admin if it was seeded." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Verify the main navigation loads and the Home, Assets, Assignments, Users, History, and Change Password pages are reachable for the signed-in role." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Sign out, sign back in, and refresh the page to confirm the client can restore the current session." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Optional: run the API tests after setup if you want an additional verification pass." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "dotnet test .\ITAMS.Api.Tests\ITAMS.Api.Tests.csproj -c Release" -Style "CodeBlock"))

$paragraphs.Add((New-ParagraphXml -Text "Troubleshooting" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "MongoDB Connection Failures" -Style "Heading2"))
$paragraphs.Add((New-ParagraphXml -Text "If the API times out or reports that it cannot connect to MongoDB, verify the MongoDb:ConnectionString user secret, Atlas network access rules, and the database user credentials. The new machine must be allowed to reach the target cluster." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "HTTPS Certificate Problems" -Style "Heading2"))
$paragraphs.Add((New-ParagraphXml -Text "If localhost HTTPS endpoints show trust warnings or fail to start cleanly, run dotnet dev-certs https --trust and restart the browser and both dotnet watch processes." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "Ports Already in Use" -Style "Heading2"))
$paragraphs.Add((New-ParagraphXml -Text "If port 7004 or 5173 is already in use, stop the conflicting process or change the launch profile ports. If you change ports or hosts, also update frontend\ITAMS.Client\wwwroot\appsettings.json and the Cors:AllowedOrigins entries in ITAMS.Api\appsettings.json." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "Login or Stale Session Problems" -Style "Heading2"))
$paragraphs.Add((New-ParagraphXml -Text "If login succeeds in the API but the browser behaves as if the session is invalid, clear the login page session state or remove the session storage key named itams.auth.session and try again." -Style "Normal"))
$paragraphs.Add((New-ParagraphXml -Text "Bootstrap Admin Not Created" -Style "Heading2"))
$paragraphs.Add((New-ParagraphXml -Text "Bootstrap admin seeding is dormant when login-capable users already exist in the configured database. In that case, use an existing account or point the API at a clean development database if you need bootstrap credentials to be created." -Style "Normal"))

$paragraphs.Add((New-ParagraphXml -Text "Operational Notes" -Style "Heading1"))
$paragraphs.Add((New-ParagraphXml -Text "Current API HTTPS URL: https://localhost:7004" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Current frontend HTTPS URL: https://localhost:5173" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Current frontend ApiBaseUrl: https://localhost:7004/" -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Development access tokens are configured for 15 minutes and refresh tokens are configured for 4 hours." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "Keep placeholders in documentation and put real secrets only into the destination machine's user secrets store." -NumberId 1))
$paragraphs.Add((New-ParagraphXml -Text "This guide is for local development and handoff, not for production deployment." -NumberId 1))

$joinedParagraphs = $paragraphs -join [Environment]::NewLine

$documentXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    $joinedParagraphs
    <w:sectPr>
      <w:pgSz w:w="12240" w:h="15840"/>
      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
    </w:sectPr>
  </w:body>
</w:document>
"@

$stylesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:docDefaults>
    <w:rPrDefault>
      <w:rPr>
        <w:rFonts w:ascii="Aptos" w:hAnsi="Aptos" w:cs="Aptos"/>
        <w:sz w:val="22"/>
        <w:szCs w:val="22"/>
        <w:color w:val="1F2933"/>
      </w:rPr>
    </w:rPrDefault>
    <w:pPrDefault>
      <w:pPr>
        <w:spacing w:after="150" w:line="280" w:lineRule="auto"/>
      </w:pPr>
    </w:pPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Title">
    <w:name w:val="Title"/>
    <w:basedOn w:val="Normal"/>
    <w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr>
      <w:spacing w:after="220"/>
    </w:pPr>
    <w:rPr>
      <w:b/>
      <w:color w:val="15364A"/>
      <w:sz w:val="34"/>
      <w:szCs w:val="34"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Subtitle">
    <w:name w:val="Subtitle"/>
    <w:basedOn w:val="Normal"/>
    <w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr>
      <w:spacing w:after="220"/>
    </w:pPr>
    <w:rPr>
      <w:color w:val="5B6B75"/>
      <w:sz w:val="20"/>
      <w:szCs w:val="20"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="Heading 1"/>
    <w:basedOn w:val="Normal"/>
    <w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr>
      <w:spacing w:before="240" w:after="120"/>
    </w:pPr>
    <w:rPr>
      <w:b/>
      <w:color w:val="15364A"/>
      <w:sz w:val="28"/>
      <w:szCs w:val="28"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="Heading 2"/>
    <w:basedOn w:val="Normal"/>
    <w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr>
      <w:spacing w:before="180" w:after="80"/>
    </w:pPr>
    <w:rPr>
      <w:b/>
      <w:color w:val="27485C"/>
      <w:sz w:val="24"/>
      <w:szCs w:val="24"/>
    </w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="CodeBlock">
    <w:name w:val="Code Block"/>
    <w:basedOn w:val="Normal"/>
    <w:next w:val="Normal"/>
    <w:qFormat/>
    <w:pPr>
      <w:spacing w:before="40" w:after="40"/>
      <w:ind w:left="240" w:right="120"/>
    </w:pPr>
    <w:rPr>
      <w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:cs="Consolas"/>
      <w:color w:val="17384B"/>
      <w:sz w:val="19"/>
      <w:szCs w:val="19"/>
    </w:rPr>
  </w:style>
</w:styles>
"@

$numberingXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:abstractNum w:abstractNumId="0">
    <w:multiLevelType w:val="hybridMultilevel"/>
    <w:lvl w:ilvl="0">
      <w:start w:val="1"/>
      <w:numFmt w:val="bullet"/>
      <w:lvlText w:val="•"/>
      <w:lvlJc w:val="left"/>
      <w:pPr>
        <w:ind w:left="720" w:hanging="360"/>
      </w:pPr>
      <w:rPr>
        <w:rFonts w:ascii="Aptos" w:hAnsi="Aptos" w:cs="Aptos"/>
      </w:rPr>
    </w:lvl>
  </w:abstractNum>
  <w:num w:numId="1">
    <w:abstractNumId w:val="0"/>
  </w:num>
</w:numbering>
"@

$documentRelationshipsXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
</Relationships>
"@

$packageRelationshipsXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
"@

$contentTypesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
"@

$corePropertiesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>ITAMS Setup Runbook</dc:title>
  <dc:subject>Cross-machine setup and handoff guide</dc:subject>
  <dc:creator>Codex</dc:creator>
  <cp:lastModifiedBy>Codex</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">$generatedOnUtc</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">$generatedOnUtc</dcterms:modified>
</cp:coreProperties>
"@

$extendedPropertiesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>Codex PowerShell Open XML Generator</Application>
  <DocSecurity>0</DocSecurity>
  <ScaleCrop>false</ScaleCrop>
  <Company></Company>
  <LinksUpToDate>false</LinksUpToDate>
  <SharedDoc>false</SharedDoc>
  <HyperlinksChanged>false</HyperlinksChanged>
  <AppVersion>1.0</AppVersion>
</Properties>
"@

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("itams-runbook-" + [System.Guid]::NewGuid().ToString("N"))

try
{
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot "_rels") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot "docProps") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot "word") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot "word\_rels") -Force | Out-Null

    Write-Utf8File -Path (Join-Path $stagingRoot "[Content_Types].xml") -Content $contentTypesXml
    Write-Utf8File -Path (Join-Path $stagingRoot "_rels\.rels") -Content $packageRelationshipsXml
    Write-Utf8File -Path (Join-Path $stagingRoot "docProps\core.xml") -Content $corePropertiesXml
    Write-Utf8File -Path (Join-Path $stagingRoot "docProps\app.xml") -Content $extendedPropertiesXml
    Write-Utf8File -Path (Join-Path $stagingRoot "word\document.xml") -Content $documentXml
    Write-Utf8File -Path (Join-Path $stagingRoot "word\styles.xml") -Content $stylesXml
    Write-Utf8File -Path (Join-Path $stagingRoot "word\numbering.xml") -Content $numberingXml
    Write-Utf8File -Path (Join-Path $stagingRoot "word\_rels\document.xml.rels") -Content $documentRelationshipsXml

    if (Test-Path $OutputPath)
    {
        Remove-Item -Path $OutputPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $OutputPath)
    Write-Host "Generated $OutputPath"
}
finally
{
    if (Test-Path $stagingRoot)
    {
        Remove-Item -Path $stagingRoot -Recurse -Force
    }
}
