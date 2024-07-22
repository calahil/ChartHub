namespace RhythmVerseClient.Services
{
    public class RhythmVerseWebAPI
    {
        public RhythmVerseWebAPI()
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://rhythmverse.co/api/chm/songfiles/list");
            request.Headers.Add("Authorization", "Bearer eyJ0eXAiOiJKV1QiLCJub25jZSI6IktsMTM5OXJ6OUdEbzlDamFBNlVfNFdFUGprMmhVbWhlcGs0SGFpcDZjWVkiLCJhbGciOiJSUzI1NiIsIng1dCI6InEtMjNmYWxldlpoaEQzaG05Q1Fia1A1TVF5VSIsImtpZCI6InEtMjNmYWxldlpoaEQzaG05Q1Fia1A1TVF5VSJ9.eyJhdWQiOiIwMDAwMDAwMy0wMDAwLTAwMDAtYzAwMC0wMDAwMDAwMDAwMDAiLCJpc3MiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC83ZmM1YWYyNy1iNjZlLTRkOGEtYTNhYy05NGE1OGQ2M2ViMTYvIiwiaWF0IjoxNzE0MDEyNzQzLCJuYmYiOjE3MTQwMTI3NDMsImV4cCI6MTcxNDAxNzc2NCwiYWNjdCI6MCwiYWNyIjoiMSIsImFpbyI6IkFWUUFxLzhXQUFBQVB4OXBabk5jZDNtdFNid0o4amJlZmZiQUZFelNUUWFmUGdiMFJuZlBMUExtUnlWU2luNkRYMTd6SjZzc0p3bVMxbm8vOUZnam1ESm5hNnptenQwTEZKNEQzYXk0VEVBalZHY3RWOFZ2eWk4PSIsImFtciI6WyJwd2QiLCJtZmEiXSwiYXBwX2Rpc3BsYXluYW1lIjoiRmFpciBUaW1lIEFwcCIsImFwcGlkIjoiM2M2MzVmY2QtNjc2Zi00NzM4LTk0ZTktZjFiZTJkNDE3YWQyIiwiYXBwaWRhY3IiOiIxIiwiZmFtaWx5X25hbWUiOiJDb3dhbiIsImdpdmVuX25hbWUiOiJDaHJpc3RvcGhlciIsImlkdHlwIjoidXNlciIsImlwYWRkciI6IjIzLjI0MC4yMjYuMTU5IiwibmFtZSI6IkNocmlzdG9waGVyIENvd2FuIiwib2lkIjoiZGQ4YjM5YjgtZDRjOS00MTRlLWIzNGMtMjIxOGFlMzQ1MTc0IiwicGxhdGYiOiIzIiwicHVpZCI6IjEwMDMyMDAyQkI2QjU5MDgiLCJyaCI6IjAuQWJjQUo2X0ZmMjYyaWsyanJKU2xqV1ByRmdNQUFBQUFBQUFBd0FBQUFBQUFBQUMzQU5FLiIsInNjcCI6IlVzZXIuUmVhZCBwcm9maWxlIG9wZW5pZCBlbWFpbCIsInN1YiI6InZkNmRfbV9oZUtFejBpZzNyMUxLRngtUWFFSmtzQWZyMmZRcnNaZDhWQkEiLCJ0ZW5hbnRfcmVnaW9uX3Njb3BlIjoiTkEiLCJ0aWQiOiI3ZmM1YWYyNy1iNjZlLTRkOGEtYTNhYy05NGE1OGQ2M2ViMTYiLCJ1bmlxdWVfbmFtZSI6ImNhbGFoaWxAY2FsYWhpbHN0dWRpb3MuY29tIiwidXBuIjoiY2FsYWhpbEBjYWxhaGlsc3R1ZGlvcy5jb20iLCJ1dGkiOiJCMEtMX19XaTgwYWMyRTduTC1EUUFBIiwidmVyIjoiMS4wIiwid2lkcyI6WyI2MmU5MDM5NC02OWY1LTQyMzctOTE5MC0wMTIxNzcxNDVlMTAiLCJiNzlmYmY0ZC0zZWY5LTQ2ODktODE0My03NmIxOTRlODU1MDkiXSwieG1zX3N0Ijp7InN1YiI6IjQtQml4UmVreUg0dVJvNHJZaktDZUJYQi0ya3Roclh3N1ZwcFZaWW5oUjgifSwieG1zX3RjZHQiOjE2ODg0NTI4NDN9.ramDMKQ6wV2TGz5BpmbJCvUAwdtUyfBQh2SjsXebFrklutCqdtoNQRaPRIuI8DwsbcXSDXHVDLfnz7HzCVLNBE0r9Tg6GEjucEuo0j1Z2HVIvS2urlwfABHjq6SvSS8OcttCSSgReFNAP_KJhIkMnztmqykDoZBhX80kOvU2gCJtCtX2SFpyPjzAw8Bz-iZJFL9K-9CLbmSnLP2lnS_1anwyCQilSpapNNhf-ucsKzYPRXWkgYyV4WUPmseUc3yd8tLYW9w92hPspwN_7jqhqykz-4gOyNe3Hd2lOatOC_cVndczymlRnDttYoNZ--Z9sYXBIG0BWA6UG1gT_pUaqg");
            var collection = new List<KeyValuePair<string, string>>();
            collection.Add(new("instrument", "drums"));
            collection.Add(new("sort[0][sort_by]", "update_date"));
            collection.Add(new("sort[0][sort_order]", "DESC"));
            collection.Add(new("data_type", "full"));
            collection.Add(new("page", "1"));
            collection.Add(new("records", "25"));
            var content = new FormUrlEncodedContent(collection);
            request.Content = content;
            //var response = await client.SendAsync(request);
            //response.EnsureSuccessStatusCode();
            //Console.WriteLine(await response.Content.ReadAsStringAsync());

        }
    }
}
