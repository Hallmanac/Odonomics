namespace Odonomics.Walk;

/// <summary>Splits a scenario's "Make Model" value (e.g. "Toyota Camry Hybrid") into its make and
/// model halves on the first space; everything after the first word is the model, since a model
/// itself can contain a space ("Camry Hybrid").</summary>
public static class MakeModel
{
    public static (string Make, string Model) Split(string makeModel)
    {
        int spaceIndex = makeModel.IndexOf(' ');
        if (spaceIndex < 0)
        {
            throw new InvalidOperationException($"--model must be \"Make Model\" (e.g. \"Honda Insight\"), got \"{makeModel}\"");
        }

        return (makeModel[..spaceIndex], makeModel[(spaceIndex + 1)..]);
    }
}
