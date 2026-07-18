namespace Nornis.Domain.Enums;

public enum ReviewChangeType
{
    CreateArtifact,
    UpdateArtifact,
    MergeArtifact,
    AddFact,
    UpdateFact,
    AddRelationship,
    UpdateRelationship,

    /// <summary>Pin an artifact onto a map image at a normalized position.</summary>
    AddPlacemark
}
