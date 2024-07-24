
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

        public RhythmVerseApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://rhythmverse.co");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer","eyJ0eXAiOiJKV1QiLCJub25jZSI6IktsMTM5OXJ6OUdEbzlDamFBNlVfNFdFUGprMmhVbWhlcGs0SGFpcDZjWVkiLCJhbGciOiJSUzI1NiIsIng1dCI6InEtMjNmYWxldlpoaEQzaG05Q1Fia1A1TVF5VSIsImtpZCI6InEtMjNmYWxldlpoaEQzaG05Q1Fia1A1TVF5VSJ9.eyJhdWQiOiIwMDAwMDAwMy0wMDAwLTAwMDAtYzAwMC0wMDAwMDAwMDAwMDAiLCJpc3MiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC83ZmM1YWYyNy1iNjZlLTRkOGEtYTNhYy05NGE1OGQ2M2ViMTYvIiwiaWF0IjoxNzE0MDEyNzQzLCJuYmYiOjE3MTQwMTI3NDMsImV4cCI6MTcxNDAxNzc2NCwiYWNjdCI6MCwiYWNyIjoiMSIsImFpbyI6IkFWUUFxLzhXQUFBQVB4OXBabk5jZDNtdFNid0o4amJlZmZiQUZFelNUUWFmUGdiMFJuZlBMUExtUnlWU2luNkRYMTd6SjZzc0p3bVMxbm8vOUZnam1ESm5hNnptenQwTEZKNEQzYXk0VEVBalZHY3RWOFZ2eWk4PSIsImFtciI6WyJwd2QiLCJtZmEiXSwiYXBwX2Rpc3BsYXluYW1lIjoiRmFpciBUaW1lIEFwcCIsImFwcGlkIjoiM2M2MzVmY2QtNjc2Zi00NzM4LTk0ZTktZjFiZTJkNDE3YWQyIiwiYXBwaWRhY3IiOiIxIiwiZmFtaWx5X25hbWUiOiJDb3dhbiIsImdpdmVuX25hbWUiOiJDaHJpc3RvcGhlciIsImlkdHlwIjoidXNlciIsImlwYWRkciI6IjIzLjI0MC4yMjYuMTU5IiwibmFtZSI6IkNocmlzdG9waGVyIENvd2FuIiwib2lkIjoiZGQ4YjM5YjgtZDRjOS00MTRlLWIzNGMtMjIxOGFlMzQ1MTc0IiwicGxhdGYiOiIzIiwicHVpZCI6IjEwMDMyMDAyQkI2QjU5MDgiLCJyaCI6IjAuQWJjQUo2X0ZmMjYyaWsyanJKU2xqV1ByRmdNQUFBQUFBQUFBd0FBQUFBQUFBQUMzQU5FLiIsInNjcCI6IlVzZXIuUmVhZCBwcm9maWxlIG9wZW5pZCBlbWFpbCIsInN1YiI6InZkNmRfbV9oZUtFejBpZzNyMUxLRngtUWFFSmtzQWZyMmZRcnNaZDhWQkEiLCJ0ZW5hbnRfcmVnaW9uX3Njb3BlIjoiTkEiLCJ0aWQiOiI3ZmM1YWYyNy1iNjZlLTRkOGEtYTNhYy05NGE1OGQ2M2ViMTYiLCJ1bmlxdWVfbmFtZSI6ImNhbGFoaWxAY2FsYWhpbHN0dWRpb3MuY29tIiwidXBuIjoiY2FsYWhpbEBjYWxhaGlsc3R1ZGlvcy5jb20iLCJ1dGkiOiJCMEtMX19XaTgwYWMyRTduTC1EUUFBIiwidmVyIjoiMS4wIiwid2lkcyI6WyI2MmU5MDM5NC02OWY1LTQyMzctOTE5MC0wMTIxNzcxNDVlMTAiLCJiNzlmYmY0ZC0zZWY5LTQ2ODktODE0My03NmIxOTRlODU1MDkiXSwieG1zX3N0Ijp7InN1YiI6IjQtQml4UmVreUg0dVJvNHJZaktDZUJYQi0ya3Roclh3N1ZwcFZaWW5oUjgifSwieG1zX3RjZHQiOjE2ODg0NTI4NDN9.ramDMKQ6wV2TGz5BpmbJCvUAwdtUyfBQh2SjsXebFrklutCqdtoNQRaPRIuI8DwsbcXSDXHVDLfnz7HzCVLNBE0r9Tg6GEjucEuo0j1Z2HVIvS2urlwfABHjq6SvSS8OcttCSSgReFNAP_KJhIkMnztmqykDoZBhX80kOvU2gCJtCtX2SFpyPjzAw8Bz-iZJFL9K-9CLbmSnLP2lnS_1anwyCQilSpapNNhf-ucsKzYPRXWkgYyV4WUPmseUc3yd8tLYW9w92hPspwN_7jqhqykz-4gOyNe3Hd2lOatOC_cVndczymlRnDttYoNZ--Z9sYXBIG0BWA6UG1gT_pUaqg");
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