using Nornis.Web.ApiClient;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The Settings page's form (Nornis 3.23): each section's Save sends only its own fields, every
/// other one null so the API's partial update leaves it as it is.
/// </summary>
[TestFixture]
public class WorldSettingsFormTests
{
    private static readonly WorldSummary World = new(
        Guid.NewGuid(), "Black Harbor", "A port of smugglers.", "D&D 5e", "GM",
        DailyAiBudgetUsd: 2m, PublicSlug: "black-harbor", PublicAccessEnabled: true,
        PublicAskMonthlyBudgetUsd: 5m, SummaryReviewRequired: true, IsTemplate: false,
        PrepMonthlyBudgetUsd: 10m);

    [Test]
    public void TheForm_StartsFromTheWorld()
    {
        var form = WorldSettingsForm.From(World);

        Assert.Multiple(() =>
        {
            Assert.That(form.WorldId, Is.EqualTo(World.Id));
            Assert.That(form.Name, Is.EqualTo("Black Harbor"));
            Assert.That(form.PublicSlug, Is.EqualTo("black-harbor"));
            Assert.That(form.DailyAiBudget, Is.EqualTo(2m));
            Assert.That(form.PrepMonthlyBudget, Is.EqualTo(10m));
            Assert.That(form.SummaryReviewRequired, Is.True);
        });
    }

    [Test]
    public void ACardIsMarkedUnsaved_OnlyWhenItsOwnSaveWouldChangeTheWorld()
    {
        // Each card saves alone, and a value typed and not saved survived a tab switch with
        // nothing to say so (3.32). The mark must follow what Save would send: a trailing
        // space, or a slug in other capitals, is not a change, and neither is another card's.
        var form = WorldSettingsForm.From(World);
        Assert.That(Enum.GetValues<WorldSettingsSection>().Where(s => form.IsChanged(s, World)), Is.Empty, "a fresh form has nothing to save");

        form.Name = "Black Harbor ";
        form.PublicSlug = " Black-Harbor";
        Assert.That(Enum.GetValues<WorldSettingsSection>().Where(s => form.IsChanged(s, World)), Is.Empty);

        form.DailyAiBudget = null;
        Assert.Multiple(() =>
        {
            Assert.That(form.IsChanged(WorldSettingsSection.Budgets, World), Is.True, "clearing a budget is a change");
            Assert.That(form.IsChanged(WorldSettingsSection.World, World), Is.False);
            Assert.That(form.IsChanged(WorldSettingsSection.PublicPage, World), Is.False);
        });
    }

    [Test]
    public void SavingThePublicPage_NeverSendsANameTypedOnAnotherTab()
    {
        var form = WorldSettingsForm.From(World);
        form.Name = "Black Harb";
        form.PublicAccessEnabled = false;

        var request = form.RequestFor(WorldSettingsSection.PublicPage);

        Assert.That(request, Is.EqualTo(new UpdateWorldRequest(
            PublicSlug: "black-harbor", PublicAccessEnabled: false, PublicAskMonthlyBudgetUsd: 5m)));
    }

    [Test]
    public void SavingTheWorld_SendsOnlyNameDescriptionAndSystem_Trimmed()
    {
        var form = WorldSettingsForm.From(World);
        form.Name = "  Black Harbor  ";
        form.DailyAiBudget = null;

        var request = form.RequestFor(WorldSettingsSection.World);

        Assert.That(request, Is.EqualTo(new UpdateWorldRequest(
            Name: "Black Harbor", Description: "A port of smugglers.", GameSystem: "D&D 5e")));
    }

    /// <summary>Null means "unchanged" to the API, so an emptied field goes as empty text.</summary>
    [Test]
    public void AnEmptiedDescription_IsSentAsEmpty_SoItClears()
    {
        var form = WorldSettingsForm.From(World);
        form.Description = null;
        form.GameSystem = null;

        var request = form.RequestFor(WorldSettingsSection.World);

        Assert.Multiple(() =>
        {
            Assert.That(request.Description, Is.Empty);
            Assert.That(request.GameSystem, Is.Empty);
        });
    }

    [Test]
    public void AnEmptiedBudget_IsSentAsItsClearFlag()
    {
        var form = WorldSettingsForm.From(World);
        form.DailyAiBudget = null;

        var request = form.RequestFor(WorldSettingsSection.Budgets);

        Assert.That(request, Is.EqualTo(new UpdateWorldRequest(
            ClearDailyAiBudget: true, PrepMonthlyBudgetUsd: 10m)));
    }

    [Test]
    public void AnEmptiedPublicAskBudget_SwitchesPublicQuestionsOff()
    {
        var form = WorldSettingsForm.From(World);
        form.PublicAskMonthlyBudget = null;

        var request = form.RequestFor(WorldSettingsSection.PublicPage);

        Assert.Multiple(() =>
        {
            Assert.That(request.PublicAskMonthlyBudgetUsd, Is.Null);
            Assert.That(request.ClearPublicAskBudget, Is.True);
        });
    }

    /// <summary>The API can't clear a slug, so an emptied one is left as it was, as before 3.23.</summary>
    [Test]
    public void AnEmptiedSlug_IsLeftAsItWas()
    {
        var form = WorldSettingsForm.From(World);
        form.PublicSlug = "  ";

        Assert.That(form.RequestFor(WorldSettingsSection.PublicPage).PublicSlug, Is.Null);
    }

    [Test]
    public void TheSwitches_EachSaveAlone()
    {
        var form = WorldSettingsForm.From(World);
        form.SummaryReviewRequired = false;
        form.IsTemplate = true;

        Assert.Multiple(() =>
        {
            Assert.That(form.RequestFor(WorldSettingsSection.Review), Is.EqualTo(new UpdateWorldRequest(SummaryReviewRequired: false)));
            Assert.That(form.RequestFor(WorldSettingsSection.Template), Is.EqualTo(new UpdateWorldRequest(IsTemplate: true)));
        });
    }

    [Test]
    public void ABlankName_StopsOnlyTheWorldSave()
    {
        var form = WorldSettingsForm.From(World);
        form.Name = " ";

        Assert.Multiple(() =>
        {
            Assert.That(form.Problem(WorldSettingsSection.World), Is.EqualTo("Name is required."));
            Assert.That(form.Problem(WorldSettingsSection.PublicPage), Is.Null);
        });
    }
}
