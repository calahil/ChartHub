
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

        public async Task<RootResponse> GetSongFilesAsync(int page, int recordsPerPage, string search, string sort)
        {
            try
            {
                string endpoint;
                //string payload;

                if (search != string.Empty)
                {
                    endpoint = "api/all/songfiles/search/live";
                }
                else
                {
                    endpoint = "api/all/songfiles/list";
                }

                try
                {
                    var collection = new List<KeyValuePair<string, string>>();
                    collection.Add(new("sort[0][sort_by]", "artist"));
                    collection.Add(new("sort[0][sort_order]", "ASC"));
                    collection.Add(new("data_type", "full"));
                    if (!string.IsNullOrEmpty(search))
                    {
                        collection.Add(new("text", $"{search}"));
                    }
                    collection.Add(new("page", $"{page}"));
                    collection.Add(new("records", $"{recordsPerPage}"));
                    var content = new FormUrlEncodedContent(collection);

                    HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();

                    ResponseBody = responseBody;

                    DecodedResponse = RootResponse.FromJson(responseBody);

                    return DecodedResponse;
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Request error: {e.Message}");
                    return new RootResponse();
                }
            }

               
            catch (Exception ex)
            {
                // Handle exceptions
                Logger.LogMessage($"An error occurred: {ex.Message}");
                return new RootResponse();
            }
        }

    }
}