using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class PriusPrimeVariantTests
{
    [Theory]
    [InlineData("Prius Prime", null)]
    [InlineData("Prius Prime", "Plug-in Hybrid")]
    [InlineData("Prius Plug-in Hybrid", "Plug-in Hybrid")]
    [InlineData("Prius Plug-in Hybrid", "Plug-In Hybrid")]
    [InlineData("Prius", "Plug-in Hybrid")]
    [InlineData("Prius Plug-in Hybrid", null)]
    [InlineData("Prius Plug-in Hybrid", "Hybrid")]
    [InlineData("Prius Plug In Hybrid", null)]
    [InlineData("Prius", "Plug In Hybrid")]
    public void ReadsAsPrime_ModelNamingPrimeOrFuelTypePlugInHybrid_Matches(string model, string? fuelType)
    {
        Assert.True(PriusPrimeVariant.ReadsAsPrime("Toyota", model, fuelType));
    }

    [Fact]
    public void ReadsAsPrime_PlainPriusNoPlugInFuelType_Rejected()
    {
        Assert.False(PriusPrimeVariant.ReadsAsPrime("Toyota", "Prius", "Hybrid"));
        Assert.False(PriusPrimeVariant.ReadsAsPrime("Toyota", "Prius", null));
    }

    [Fact]
    public void ReadsAsPrime_ModelDoesNotNamePrius_Rejected()
    {
        // A RAV4 Prime, or any other Toyota plug-in hybrid, is never mistaken for a Prius Prime.
        Assert.False(PriusPrimeVariant.ReadsAsPrime("Toyota", "RAV4 Prime", "Plug-in Hybrid"));
    }

    [Fact]
    public void ReadsAsPrime_OtherMake_Rejected()
    {
        Assert.False(PriusPrimeVariant.ReadsAsPrime("Honda", "Prius Prime", "Plug-in Hybrid"));
    }

    [Fact]
    public void ReadsAsPrime_NullMake_JudgedOnModelAndFuelTypeAlone()
    {
        Assert.True(PriusPrimeVariant.ReadsAsPrime(null, "Prius Prime", null));
    }

    [Fact]
    public void ReadsAsPrime_NullModel_Rejected()
    {
        Assert.False(PriusPrimeVariant.ReadsAsPrime("Toyota", null, "Plug-in Hybrid"));
    }

    [Fact]
    public void DecideOutcome_PlainPrius_IsNotAPrimeRegardlessOfWhetherItsKnown()
    {
        Assert.Equal(PrimeOutcome.NotAPrime, PriusPrimeVariant.DecideOutcome("Toyota", "Prius", "Hybrid", isKnownOnLedger: false));
        Assert.Equal(PrimeOutcome.NotAPrime, PriusPrimeVariant.DecideOutcome("Toyota", "Prius", "Hybrid", isKnownOnLedger: true));
    }

    [Fact]
    public void DecideOutcome_BrandNewPrime_IsDroppedAsNotACandidate()
    {
        // A Prime the ledger has never seen: the walk's own search just turned it up for the first
        // time, and it's dropped rather than saved, since it isn't a candidate the scenario ranks.
        Assert.Equal(PrimeOutcome.DropAsNotACandidate, PriusPrimeVariant.DecideOutcome("Toyota", "Prius Prime", null, isKnownOnLedger: false));
    }

    [Fact]
    public void DecideOutcome_PrimeAlreadyOnTheLedger_IsRelabelledRatherThanDropped()
    {
        // A Prime row already on the ledger (stored under the plain "Prius" model before this pair's
        // page was ever read as one) is only reached again under --revisit; it's relabelled Prius
        // Prime and kept on the ledger rather than dropped, so it excludes by name from then on
        // instead of continuing to pass as a plain Prius.
        Assert.Equal(PrimeOutcome.RelabelKnown, PriusPrimeVariant.DecideOutcome("Toyota", "Prius Prime", null, isKnownOnLedger: true));
    }

    [Fact]
    public void DecideOutcome_PlugInHybridWordingEitherRoute_MatchesReadsAsPrime()
    {
        Assert.Equal(PrimeOutcome.DropAsNotACandidate, PriusPrimeVariant.DecideOutcome("Toyota", "Prius", "Plug In Hybrid", isKnownOnLedger: false));
        Assert.Equal(PrimeOutcome.RelabelKnown, PriusPrimeVariant.DecideOutcome("Toyota", "Prius Plug-in Hybrid", null, isKnownOnLedger: true));
    }
}
