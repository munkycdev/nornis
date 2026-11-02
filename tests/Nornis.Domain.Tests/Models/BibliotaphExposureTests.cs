using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>What Bibliotaph's Nornis storage provider may see (feature 31, Requirement 6.3).</summary>
[TestFixture]
public class BibliotaphExposureTests
{
    [TestCase(LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Indexed, true)]
    [TestCase(LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.Stored, true)]
    [TestCase(LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.IndexFailed, true)]
    [TestCase(LibraryDocumentKind.Other, LibraryDocumentStatus.Stored, true)]
    [TestCase(LibraryDocumentKind.Sourcebook, LibraryDocumentStatus.PendingUpload, false)]
    [TestCase(LibraryDocumentKind.Map, LibraryDocumentStatus.Stored, false)]
    [TestCase(LibraryDocumentKind.Handout, LibraryDocumentStatus.Stored, false)]
    public void An_upload_is_exposed_by_kind_and_status(LibraryDocumentKind kind, LibraryDocumentStatus status, bool exposed)
    {
        var document = new LibraryDocument { Kind = kind, Status = status, Origin = LibraryDocumentOrigin.Upload };

        Assert.That(BibliotaphExposure.IsExposed(document), Is.EqualTo(exposed));
    }

    [Test]
    public void A_Prep_print_is_exposed_although_it_is_a_handout()
    {
        var print = new LibraryDocument
        {
            Kind = LibraryDocumentKind.Handout,
            Status = LibraryDocumentStatus.Stored,
            AdventureId = Guid.NewGuid(),
        };

        Assert.That(BibliotaphExposure.IsExposed(print), Is.True);
    }

    [Test]
    public void A_pending_print_is_not_exposed()
    {
        var print = new LibraryDocument
        {
            Kind = LibraryDocumentKind.Handout,
            Status = LibraryDocumentStatus.PendingUpload,
            AdventureId = Guid.NewGuid(),
        };

        Assert.That(BibliotaphExposure.IsExposed(print), Is.False);
    }

    [Test]
    public void A_Bibliotaph_row_is_never_exposed_back_to_Bibliotaph()
    {
        var row = new LibraryDocument
        {
            Kind = LibraryDocumentKind.Sourcebook,
            Status = LibraryDocumentStatus.Stored,
            Origin = LibraryDocumentOrigin.Bibliotaph,
        };

        Assert.That(BibliotaphExposure.IsExposed(row), Is.False);
    }
}
