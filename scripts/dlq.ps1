<#
.SYNOPSIS
Peek, resubmit or purge dead-lettered messages. Companion to
docs/runbooks/dead-letter-queue.md, which until now sent you to the portal.

Speaks the Service Bus REST API over a SAS token rather than loading the .NET SDK,
because a script that needs Azure.Messaging.ServiceBus and its half-dozen transitive
assemblies resolved out of the NuGet cache is a script that breaks on the machine you
reach for it from. This needs pwsh and nothing else.

Credentials come from the worker's `sb-manage` secret via az, so running this needs your
own Azure login — the same rights you would need to do it in the portal. Nothing in the
running system gains access it did not already have.

.PARAMETER Action
Peek (default, non-destructive), Resubmit, or Purge.

.PARAMETER Queue
source-extraction (default) or library-indexing.

.PARAMETER Count
How many messages to act on. Default 10.

.EXAMPLE
./scripts/dlq.ps1                                  # look
./scripts/dlq.ps1 -Action Resubmit -Count 5        # send back for another attempt
./scripts/dlq.ps1 -Action Purge -Count 100         # discard permanently
#>
param(
    [ValidateSet('Peek', 'Resubmit', 'Purge')]
    [string]$Action = 'Peek',

    [ValidateSet('source-extraction', 'library-indexing')]
    [string]$Queue = 'source-extraction',

    [int]$Count = 10,

    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'

if (-not $ConnectionString) {
    Write-Host '== Reading the sb-manage secret from ca-nornis-worker…'
    $ConnectionString = az containerapp secret show `
        --name ca-nornis-worker --resource-group rg-nornis `
        --secret-name sb-manage --query value -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $ConnectionString) {
        throw 'Could not read the sb-manage secret. Are you logged in with `az login`?'
    }
}

# Endpoint=sb://<ns>.servicebus.windows.net/;SharedAccessKeyName=<name>;SharedAccessKey=<key>
$parts = @{}
foreach ($segment in $ConnectionString -split ';') {
    $name, $value = $segment -split '=', 2
    if ($name) { $parts[$name.Trim()] = $value }
}
$namespaceHost = ([uri]$parts['Endpoint']).Host
$keyName = $parts['SharedAccessKeyName']
$key = $parts['SharedAccessKey']
$base = "https://$namespaceHost"

function New-SasToken([string]$resourceUri) {
    $expiry = [DateTimeOffset]::UtcNow.AddMinutes(20).ToUnixTimeSeconds()
    $encoded = [uri]::EscapeDataString($resourceUri)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($key))
    try {
        $signature = [Convert]::ToBase64String(
            $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$encoded`n$expiry")))
    }
    finally { $hmac.Dispose() }
    "SharedAccessSignature sr=$encoded&sig=$([uri]::EscapeDataString($signature))&se=$expiry&skn=$keyName"
}

# Single quotes throughout: '$DeadLetterQueue' would interpolate as a PowerShell variable
# in double quotes and silently address the live queue instead of its dead-letter side.
$dlqPath = $Queue + '/$DeadLetterQueue'
$headers = @{ Authorization = New-SasToken "$base/$Queue" }

function Receive-Locked {
    # POST to .../messages/head is peek-lock: the message stays put, reserved, until it is
    # completed, unlocked, or the lock lapses. DELETE would consume it outright, which is
    # not something a command called "peek" should ever do.
    $response = Invoke-WebRequest -Method Post -Uri "$base/$dlqPath/messages/head?timeout=5" `
        -Headers $headers -SkipHttpErrorCheck
    if ($response.StatusCode -eq 204) { return $null }
    if ($response.StatusCode -ge 400) { throw "Service Bus returned $($response.StatusCode): $($response.Content)" }

    [pscustomobject]@{
        Body       = $response.Content
        LockUri    = $response.Headers['Location'] | Select-Object -First 1
        Properties = ($response.Headers['BrokerProperties'] | Select-Object -First 1)
        Reason     = ($response.Headers['DeadLetterReason'] | Select-Object -First 1)
        Error      = ($response.Headers['DeadLetterErrorDescription'] | Select-Object -First 1)
    }
}

function Show-Message([int]$index, $message) {
    $preview = $message.Body
    if ($preview.Length -gt 300) { $preview = $preview.Substring(0, 300) + '…' }

    Write-Host ''
    Write-Host "-- message $index"
    if ($message.Reason) { Write-Host "   reason: $($message.Reason)" }
    if ($message.Error) { Write-Host "   detail: $($message.Error)" }
    Write-Host "   body:   $preview"
}

$handled = 0
Write-Host "== $Action up to $Count message(s) on $dlqPath"

if ($Action -eq 'Peek') {
    # Every lock is held until the walk finishes, then released together. Unlocking each
    # message as it is read would put it straight back at the head, and the next request
    # would return the same one again — one stuck message reported as ten, which is what
    # the first version of this script did.
    $locked = [System.Collections.Generic.List[object]]::new()
    try {
        for ($i = 0; $i -lt $Count; $i++) {
            $message = Receive-Locked
            if (-not $message) { break }
            $locked.Add($message)
            Show-Message $locked.Count $message
        }
        $handled = $locked.Count
    }
    finally {
        # Release in a finally so an interrupted peek does not leave the queue locked.
        # A missed unlock is not fatal either way — locks lapse on their own — but a
        # lapsed lock counts as a delivery attempt, and enough of those discard a message.
        foreach ($message in $locked) {
            try { Invoke-WebRequest -Method Put -Uri $message.LockUri -Headers $headers | Out-Null }
            catch { Write-Warning "Could not release a lock; it will expire on its own." }
        }
    }
}
else {
    # Resubmit and Purge both remove the message, so the head advances on its own.
    for ($i = 0; $i -lt $Count; $i++) {
        $message = Receive-Locked
        if (-not $message) { break }
        Show-Message ($handled + 1) $message

        if ($Action -eq 'Resubmit') {
            Invoke-WebRequest -Method Post -Uri "$base/$Queue/messages" -Headers $headers `
                -Body $message.Body -ContentType 'application/json' | Out-Null
            # Only after the copy is safely on the live queue — the other order can lose a
            # message if the send fails.
            Invoke-WebRequest -Method Delete -Uri $message.LockUri -Headers $headers | Out-Null
            Write-Host '   -> resubmitted'
        }
        else {
            Invoke-WebRequest -Method Delete -Uri $message.LockUri -Headers $headers | Out-Null
            Write-Host '   -> purged'
        }

        $handled++
    }
}

Write-Host ''
if ($handled -eq 0) {
    Write-Host "== Dead-letter queue for $Queue is empty."
}
else {
    Write-Host "== $Action complete: $handled message(s)."
    if ($Action -eq 'Resubmit') {
        Write-Host '   A message that fails again lands straight back here. If the count returns'
        Write-Host '   within minutes, the cause is deterministic — stop resubmitting.'
    }
}
