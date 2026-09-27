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
}
