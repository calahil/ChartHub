using System.Text.Json;

namespace RhythmVerseClient.Services
{
    public static class JsonCerealOptions
    {
        public static readonly JsonSerializerOptions Instance = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
    }

}
