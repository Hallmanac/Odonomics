using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class SimilarPriceNoteTests
{
    private const string Motivating = "JTDBCMFEXS3070309";

    private static PriceComparable Car(string vin, decimal? price, int year = 2025, int mileage = 80_000, string make = "Toyota", string model = "Corolla Hybrid") =>
        new(vin, year, make, model, mileage, price);

    /// <summary>The shape of rank-4 JTDBCMFEXS3070309: a 2025 Corolla Hybrid at 17,289 dollars among
    /// late-model XLEs asking about 22,000 to 24,000, twelve of them close in year and mileage.</summary>
    private static List<PriceComparable> MotivatingLedger(int similarCount = 12)
    {
        decimal[] prices = [22_000m, 22_300m, 22_500m, 22_700m, 22_800m, 22_800m, 22_800m, 22_900m, 23_000m, 23_300m, 23_600m, 24_000m];
        List<PriceComparable> cars = [Car(Motivating, 17_289m, mileage: 83_682)];
        for (int i = 0; i < similarCount; i++)
        {
            cars.Add(Car($"SIMILAR{i:00}", prices[i], year: 2024 + (i % 3), mileage: 70_000 + (i * 1_900)));
        }

        return cars;
    }

    [Fact]
    public void For_CarPricedFarBelowTwelveSimilarCars_NamesThePercentBelowAndTheMedian()
    {
        IReadOnlyDictionary<string, string> notes = SimilarPriceNote.For(MotivatingLedger());

        Assert.Equal("priced 24% below 12 similar cars (median $22,800); confirm the title", notes[Motivating]);
    }

    [Fact]
    public void For_OnlyTheCheapCarGetsANote_TheSimilarCarsThemselvesDoNot()
    {
        IReadOnlyDictionary<string, string> notes = SimilarPriceNote.For(MotivatingLedger());

        Assert.Equal([Motivating], notes.Keys);
    }

    [Fact]
    public void For_FourSimilarCars_MakesNoNote()
    {
        Assert.Empty(SimilarPriceNote.For(MotivatingLedger(similarCount: 4)));
    }

    [Fact]
    public void For_FiveSimilarCars_MakesANote()
    {
        IReadOnlyDictionary<string, string> notes = SimilarPriceNote.For(MotivatingLedger(similarCount: 5));

        Assert.Contains("% below 5 similar cars (median $", notes[Motivating]);
    }

    [Fact]
    public void For_PriceExactlyEightyPercentOfTheMedian_MakesANote()
    {
        List<PriceComparable> cars = [Car("CHEAP", 16_000m), .. Enumerable.Range(0, 5).Select(i => Car($"S{i}", 20_000m))];

        Assert.Equal("priced 20% below 5 similar cars (median $20,000); confirm the title", SimilarPriceNote.For(cars)["CHEAP"]);
    }

    [Fact]
    public void For_PriceJustAboveEightyPercentOfTheMedian_MakesNoNote()
    {
        List<PriceComparable> cars = [Car("CHEAP", 16_001m), .. Enumerable.Range(0, 5).Select(i => Car($"S{i}", 20_000m))];

        Assert.Empty(SimilarPriceNote.For(cars));
    }

    [Fact]
    public void For_EvenNumberOfSimilarCars_UsesTheMeanOfTheMiddleTwo()
    {
        List<PriceComparable> cars = [Car("CHEAP", 10_000m), Car("A", 20_000m), Car("B", 20_000m), Car("C", 22_000m), Car("D", 24_000m), Car("E", 40_000m), Car("F", 40_000m)];

        Assert.Contains("6 similar cars (median $23,000)", SimilarPriceNote.For(cars)["CHEAP"]);
    }

    [Fact]
    public void For_CarsOutsideTheYearMileageOrModelWindow_AreNotCompared()
    {
        List<PriceComparable> cars =
        [
            Car("CHEAP", 10_000m),
            Car("S1", 20_000m),
            Car("S2", 20_000m),
            Car("S3", 20_000m),
            Car("S4", 20_000m),
            Car("TWOYEARS", 20_000m, year: 2023),
            Car("MILES", 20_000m, mileage: 95_001),
            Car("PRIUS", 20_000m, model: "Prius"),
            Car("HONDA", 20_000m, make: "Honda"),
        ];

        Assert.Empty(SimilarPriceNote.For(cars));
    }

    [Fact]
    public void For_CarsAtTheEdgeOfTheWindows_AreCompared()
    {
        List<PriceComparable> cars =
        [
            Car("CHEAP", 10_000m),
            Car("YEARUP", 20_000m, year: 2026),
            Car("YEARDOWN", 20_000m, year: 2024),
            Car("MILESUP", 20_000m, mileage: 95_000),
            Car("MILESDOWN", 20_000m, mileage: 65_000),
            Car("LOWERCASE", 20_000m, make: "toyota", model: "corolla hybrid"),
        ];

        Assert.Contains("5 similar cars", SimilarPriceNote.For(cars)["CHEAP"]);
    }

    [Fact]
    public void For_SimilarCarsWithoutAPrice_DoNotCount()
    {
        List<PriceComparable> cars = [Car("CHEAP", 10_000m), .. Enumerable.Range(0, 4).Select(i => Car($"S{i}", 20_000m)), Car("NOPRICE", null)];

        Assert.Empty(SimilarPriceNote.For(cars));
    }

    [Fact]
    public void For_CarWithoutAPrice_HasNoNote()
    {
        List<PriceComparable> cars = [Car("NOPRICE", null), .. Enumerable.Range(0, 6).Select(i => Car($"S{i}", 20_000m))];

        Assert.Empty(SimilarPriceNote.For(cars));
    }
}
