using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class AddOnsDealerFlagTests
{
    [Fact]
    public void AddOnsDealer_UnknownPostureAtADealerWithDollarAddOns_FlagsItNamingTheDealerAndItsDocFee()
    {
        RedFlag? flag = RedFlagsEvaluator.AddOnsDealer(feePostureIsUnknown: true, "Seminole Toyota", 999m, "$358 add-ons");

        Assert.NotNull(flag);
        Assert.Equal("add-ons-dealer", flag.ShortTag);
        Assert.Contains("Seminole Toyota", flag.Detail);
        Assert.Contains("$999 doc fee", flag.Detail);
        Assert.Contains("$358 add-ons", flag.Detail);
    }

    [Fact]
    public void AddOnsDealer_DealerWithNoKnownDocFee_FlagsWithoutMentioningOne()
    {
        RedFlag? flag = RedFlagsEvaluator.AddOnsDealer(feePostureIsUnknown: true, "Seminole Toyota", null, "$1,358 add-ons");

        Assert.NotNull(flag);
        Assert.DoesNotContain("doc fee", flag.Detail);
        Assert.Contains("$1,358 add-ons", flag.Detail);
    }

    [Fact]
    public void AddOnsDealer_PostureThatIsNotUnknown_NeverFires()
    {
        Assert.Null(RedFlagsEvaluator.AddOnsDealer(feePostureIsUnknown: false, "Seminole Toyota", 999m, "$358 add-ons"));
    }

    [Theory]
    [InlineData("No add-ons")]
    [InlineData("$0 add-ons")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("add-ons unknown")]
    public void AddOnsDealer_DealerWhoseNoteSaysItSellsNoAddOns_DoesNotFire(string? note)
    {
        Assert.Null(RedFlagsEvaluator.AddOnsDealer(feePostureIsUnknown: true, "Daytona Toyota", 1199m, note));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void AddOnsDealer_ListingWithNoDealer_DoesNotFire(string? dealerName)
    {
        Assert.Null(RedFlagsEvaluator.AddOnsDealer(feePostureIsUnknown: true, dealerName, null, "$358 add-ons"));
    }
}
