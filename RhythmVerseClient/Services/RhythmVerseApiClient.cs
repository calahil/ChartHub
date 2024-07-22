
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

        public async Task<RootResponse> GetSongFilesAsync(int page, int recordsPerPage)
        {
            try
            {
                var endpoint = "api/chm/songfiles/list";
                var payload = $"instrument=drums&sort%5B0%5D%5Bsort_by%5D=update_date&sort%5B0%5D%5Bsort_order%5D=DESC&data_type=full&page={page}&records={recordsPerPage}";

                try
                {
                    var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

                    HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();

                    var decodedResponse = RootResponse.FromJson(responseBody);

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
