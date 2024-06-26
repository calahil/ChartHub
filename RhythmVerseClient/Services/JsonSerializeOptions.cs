using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
