namespace RhythmVerseClient.Tests.TestInfrastructure;

public static class TestIds
{
    public static string Next(string prefix = "test")
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}
