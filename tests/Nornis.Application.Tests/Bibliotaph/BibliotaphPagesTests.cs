using Nornis.Application.Bibliotaph;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Bibliotaph;

[TestFixture]
public class BibliotaphPagesTests
{
    [TestCase(0, 1)]
    [TestCase(194, 195)]
    public void A_page_index_is_one_less_than_its_page_number(int index, int number)
    {
        Assert.That(BibliotaphPages.ToPageNumber(index), Is.EqualTo(number));
        Assert.That(BibliotaphPages.ToPageIndex(number), Is.EqualTo(index));
    }

    [Test]
    public void Page_zero_is_not_a_page_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BibliotaphPages.ToPageIndex(0));
    }

    [TestCase(BibliotaphWorldEventKind.Created, "created")]
    [TestCase(BibliotaphWorldEventKind.Renamed, "renamed")]
    [TestCase(BibliotaphWorldEventKind.Deleted, "deleted")]
    [TestCase(BibliotaphWorldEventKind.FilesChanged, "files-changed")]
    public void Event_kinds_go_out_under_the_contracts_names(BibliotaphWorldEventKind kind, string wire)
    {
        Assert.That(BibliotaphWireNames.Of(kind), Is.EqualTo(wire));
    }

    [Test]
    public void Every_event_kind_has_a_wire_name()
    {
        foreach (var kind in Enum.GetValues<BibliotaphWorldEventKind>())
        {
            Assert.That(() => BibliotaphWireNames.Of(kind), Throws.Nothing, kind.ToString());
        }
    }
}
