
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Api;
using RhythmVerseClient.Utilities;
using System.Net.Http.Headers;
using System.Text;
using Windows.Media.Protection.PlayReady;

namespace RhythmVerseClient.Services
{
    public class RhythmVerseApiClient
    {
        private readonly HttpClient _httpClient;

        public string ResponseBody { get; private set; } = string.Empty;

        public RootResponse? DecodedResponse { get; private set; }

        public RhythmVerseApiClient(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://rhythmverse.co");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration["rhythmverseToken"]);
        }

        public async Task<RootResponse> GetSongFilesAsync(int page, int recordsPerPage, string search)
        {
            try
            {
                string endpoint;
                //string payload;

                if (search != string.Empty)
                {
                    endpoint = "api/all/songfiles/search/live";
                    //payload = $"sort%5B0%5D%5Bsort_by%5D=title&sort%5B0%5D%5Bsort_order%5D=ASC&data_type=full&text={search}&page={page}&records={recordsPerPage}";
                }
                else
                {
                    endpoint = "api/all/songfiles/list";
                    //payload = $"sort%5B0%5D%5Bsort_by%5D=title&sort%5B0%5D%5Bsort_order%5D=ASC&data_type=full&page={page}&records={recordsPerPage}";
                }

                try
                {
                    //var request = new HttpRequestMessage(HttpMethod.Post, _httpClient.BaseAddress + endpoint);
                    //request.Headers.Add("Authorization", );
                    var collection = new List<KeyValuePair<string, string>>();
                    collection.Add(new("sort[0][sort_by]", "title"));
                    collection.Add(new("sort[0][sort_order]", "DESC"));
                    collection.Add(new("data_type", "full"));
                    if (!string.IsNullOrEmpty(search))
                    {
                        collection.Add(new("text", $"{search}"));
                    }
                    collection.Add(new("page", $"{page}"));
                    collection.Add(new("records", $"{recordsPerPage}"));
                    var content = new FormUrlEncodedContent(collection);

                    //var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

                    //var response = await _httpClient.SendAsync(request);
                    HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();

                    ResponseBody = responseBody;

                    DecodedResponse = RootResponse.FromJson(responseBody);


                    Task.Delay(100);
                    return DecodedResponse;
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Request error: {e.Message}");
                    return null;
                }
            }

               
            catch (Exception ex)
            {
                // Handle exceptions
                Logger.LogMessage($"An error occurred: {ex.Message}");
                return null;
            }
        }

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