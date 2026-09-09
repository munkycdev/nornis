namespace Nornis.Api.Contracts.Requests;

/// <param name="Sheet">
/// The whole sheet, replacing whatever was there. Null or blank clears it — the service
/// normalises both to null so the field has one empty representation.
/// </param>
public record UpdateCharacterSheetRequest(string? Sheet);

public record SetCharacterSheetSharingRequest(bool SharedWithParty);
