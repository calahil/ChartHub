
using RhythmVerseClient.Api;
using RhythmVerseClient.Utilities;
using System.Text;

namespace RhythmVerseClient.Services
{
    public class RhythmVerseApiClient
    {
        private readonly HttpClient _httpClient;

        public RhythmVerseApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://rhythmverse.co/");
        }

        public async Task<RootResponse> GetSongFilesAsync(int page, int recordsPerPage, string search)
        {
            try
            {
                string endpoint;
                string payload;

                if (search != string.Empty)
                {
                    endpoint = "/api/all/songfiles/search/live";
                    payload = $"sort%5B0%5D%5Bsort_by%5D=title&sort%5B0%5D%5Bsort_order%5D=ASC&data_type=full&text={search}&page={page}&records={recordsPerPage}";
                }
                else
                {
                    endpoint = "/api/all/songfiles/list";
                    payload = $"sort%5B0%5D%5Bsort_by%5D=title&sort%5B0%5D%5Bsort_order%5D=ASC&data_type=full&page={page}&records={recordsPerPage}";
                }

                try
                {
                    var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

                    HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();

                    var decodedResponse = RootResponse.FromJson(responseBody);
                    Task.Delay(100);
                    return decodedResponse;
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Request error: {e.Message}");
                    return null;
                }
            }

               /* var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

                var response = await _httpClient.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    var decodedResponse = JsonSerializer.Deserialize<RootResponse>(jsonResponse, RhythmVerseClient.Api.Converter.Settings);
                  return decodedResponse;

                }
                else
                {
                    // Handle unsuccessful request
                    return null;
                }
            }*/
            catch (Exception ex)
            {
                // Handle exceptions
                Logger.LogError(ex);
                return null;
            }
        }

    }
}
