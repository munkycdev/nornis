<#
.SYNOPSIS
Peek, resubmit or purge dead-lettered messages. Companion to
docs/runbooks/dead-letter-queue.md, which until now sent you to the portal.

Speaks the Service Bus REST API rather than loading the .NET SDK, because a script that
needs Azure.Messaging.ServiceBus and its half-dozen transitive assemblies resolved out of
the NuGet cache is a script that breaks on the machine you reach for it from. This needs
pwsh and az and nothing else.

Authenticates as you: a bearer token from your own `az login`, minted for the Service Bus
audience. Since O3 (2026-09-08) no shared-access key exists for this — the apps reach the
namespace as their managed identities and so does this script, as yours. You need the
Azure Service Bus Data Owner role on the namespace (receiving from a dead-letter queue and
sending back to the live one are both data-plane rights); David holds it. Nothing in the
running system gains access it did not already have.

.PARAMETER Action
Peek (default, non-destructive), Resubmit, or Purge.

.PARAMETER Queue
source-extraction (default) or library-indexing.

.PARAMETER Count
How many messages to act on. Default 10.

.PARAMETER Namespace
The Service Bus namespace name. Default sb-nornis-dev.

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

    [string]$Namespace = 'sb-nornis-dev'
)

$ErrorActionPreference = 'Stop'

Write-Host '== Minting a Service Bus token from your az login…'
$token = az account get-access-token --resource 'https://servicebus.azure.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $token) {
    throw 'Could not get a Service Bus token. Are you logged in with `az login`?'
}

$base = "https://$Namespace.servicebus.windows.net"

# Single quotes throughout: '$DeadLetterQueue' would interpolate as a PowerShell variable
# in double quotes and silently address the live queue instead of its dead-letter side.
$dlqPath = $Queue + '/$DeadLetterQueue'
$headers = @{ Authorization = "Bearer $token" }

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
