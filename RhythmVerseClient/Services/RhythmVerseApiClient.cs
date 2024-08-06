
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Api;
using RhythmVerseClient.Models;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.Services
{
    public class RhythmVerseApiClient :INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, List<string>> dictionary = new()
        {
                { "rb3", ["Rock Band 3", "rb3.png"] },
                { "rv", ["RhythmVerse", "rv.png"] },
                { "rb3xbox", ["Rock Band 3 Xbox 360", "rb3xbox.png"] },
                { "rb3wii", ["Rock Band 3 Wii", "rb3wii.png"] },
                { "rb3ps3", ["Rock Band 3 PS3", "rb3ps3.png"] },
                { "wtde", ["Guitar Hero World Tour: Definitive Edition", "wtde.png"] },
                { "tbrbxbox", ["The Beatles: Rock Band XBox 360", "tbrb.png"] },
                { "tbrbps3", ["The Beatles: Rock Band PS3", "tbrb.png"] },
                { "tbrb", ["The Beatles: Rock Band", "tbrb.png"] },
                { "yarg", ["YARG", "yarg.png"] },
                { "rb2xbox", ["Rock Band 2 Xbox 360", "rb2.png"] },
                { "ps", ["Phase Shift", "ps.png"] },
                { "chm", ["Clone Hero", "ch.png"] },
                { "ch", ["Clone Hero", "ch.png"] },
                { "gh3pc", ["Guitar Hero World Tour PC", "gh.png"] }
            };

        private const string BaseUrl = "https://rhythmverse.co";

        private readonly List<string> Ratings = [
                "OOOOO",
                "\u2B24" + "OOOO",
                "\u2B24" + "\u2B24" + "OOO",
                "\u2B24" + "\u2B24" +"\u2B24" + "OO",
                "\u2B24" + "\u2B24" + "\u2B24" + "\u2B24" + "O",
                "\u2B24" + "\u2B24" + "\u2B24" + "\u2B24" + "\u2B24"
            ];

        private ObservableCollection<ViewSong>? _dataItems;
        public ObservableCollection<ViewSong>? DataItems
        {
            get => _dataItems;
            set
            {
                _dataItems = value;
                OnPropertyChanged();
            }
        }

        public long? RecordsPerPage { get; } = 25;


        private bool _hasMoreRecords = true;
        public bool HasMoreRecords
        {
            get => _hasMoreRecords;
            set
            {
                _hasMoreRecords = value;
                OnPropertyChanged();
            }
        }

        private long? _totalPages;
        public long? TotalPages
        {
            get => _totalPages;
            set
            {
                _totalPages = value;
                OnPropertyChanged();
            }
        }

        private long? _totalResults;
        public long? TotalResults
        {
            get => _totalResults;
            set
            {
                _totalResults = value;
                OnPropertyChanged();
            }
        }

        private long? _currentPage;
        public long? CurrentPage
        {
            get => _currentPage;
            set
            {
                _currentPage = value;
                OnPropertyChanged();
            }
        }

        private long? _startRecord;
        public long? StartRecord
        {
            get => _startRecord;
            set
            {
                _startRecord = value;
                OnPropertyChanged();
            }
        }

        private long? _endRecord;
        public long? EndRecord
        {
            get => _endRecord;
            set
            {
                _endRecord = value;
                OnPropertyChanged();
            }
        }

        public RhythmVerseApiClient(IConfiguration configuration)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration["rhythmverseToken"]);

            _currentPage = 1;

        }

        public async Task<ObservableCollection<ViewSong>> GetSongFilesAsync(bool search, string searchString, string sort, string order, List<InstrumentItem> instrument)
        {
            if (search)
            {
                CurrentPage = 1;
                HasMoreRecords = true;
            }
            try
            {
                string endpoint;
                //string payload;

                if (searchString != string.Empty)
                {
                    endpoint = "api/all/songfiles/search/live";
                }
                else
                {
                    endpoint = "api/all/songfiles/list";
                }

                try
                {

                    if (DataItems == null)
                        DataItems = [];
                    else
                        DataItems.Clear();


                    var collection = new List<KeyValuePair<string, string>>();
                    foreach (var item in instrument)
                    {

                        if (!string.IsNullOrEmpty(item.Value))
                        {
                            collection.Add(new("instrument", $"{item.Value}"));
                        }
                    }
                    collection.Add(new("sort[0][sort_by]", $"{sort}"));
                    collection.Add(new("sort[0][sort_order]", $"{order}"));
                    collection.Add(new("data_type", "full"));
                    if (!string.IsNullOrEmpty(searchString))
                    {
                        collection.Add(new("text", $"{searchString}"));
                    }
                    collection.Add(new("page", $"{CurrentPage}"));
                    collection.Add(new("records", $"{RecordsPerPage}"));
                    var content = new FormUrlEncodedContent(collection);

                    HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();

                    var DecodedResponse = RootResponse.FromJson(responseBody);

                   if (DecodedResponse != null && DecodedResponse.Data != null)
                    {
                        if (DecodedResponse.Data.Records.TotalFiltered != null)
                        {

                            TotalResults = DecodedResponse.Data.Records.TotalFiltered;
                            TotalPages = TotalResults / RecordsPerPage;
                            if ((TotalResults % RecordsPerPage) > 0)
                                TotalPages++;
                        }

                        if (DecodedResponse.Data.Pagination.Start + 1 > 0)
                        {
                            StartRecord = DecodedResponse.Data.Pagination.Start + 1;
                            EndRecord = StartRecord + RecordsPerPage - 1;
                        }

                        foreach (var song in DecodedResponse.Data.Songs)
                        {
                            if (!song.File.DownloadUrl.StartsWith("http://marketplace.xbox.com") && !song.File.DownloadUrl.StartsWith("none"))
                            {
                                var songView = new ViewSong
                                {
                                    //Artist = song.File.FileArtist ?? song.File.FileName ?? song.File.Filename ?? "Unknown",
                                    //Title = song.File.FileTitle ?? song.File.FileName ?? song.File.Filename ?? "Unknown",
                                    //Album = song.File.FileAlbum ?? song.File.FileName ?? song.File.Filename ?? "Unknown"
                                };
                                var image = song.File.AlbumArt;
                                if (image != null)
                                {
                                    if (!image.StartsWith(BaseUrl))
                                    {
                                        image = BaseUrl + image;
                                    }
                                    songView.AlbumArt = image;
                                }

                                songView.Downloads = song.File.Downloads != 0 ? song.File.Downloads : song.Data.DataData.Downloads;
                                songView.FileName = song.File.FileName ?? song.File.Filename ?? "missing";
                                /*if (song.File.FileSongLength is null)
                                {
                                    songView.SongLength = 0;
                                }
                                else
                                {*/
                                    songView.SongLength = song.File.FileSongLength as long?;
                               // }

                                songView.FileSize = song.File.Size;
                                songView.FormattedTme = Toolbox.ConvertSecondstoText(songView.SongLength);
                                Author author = song.File.Author ?? new Author();
                                songView.Author = author.Name;

                                var avatarPath = author.AvatarPath;
                                if (avatarPath != null)
                                {
                                    if (!avatarPath.StartsWith("http"))
                                    {
                                        avatarPath = BaseUrl + avatarPath;
                                    }

                                    songView.Avatar = avatarPath;
                                }
                                else
                                {
                                    songView.Avatar = "blankprofile.png";
                                }

                                if (!song.File.DownloadUrl.StartsWith("http"))
                                {
                                    songView.DownloadLink = BaseUrl + song.File.DownloadUrl;
                                }
                                else
                                {
                                    songView.DownloadLink = song.File.DownloadUrl;
                                }

                                if (song.File.DiffDrums.HasValue && song.File.DiffDrums.Value >= 0 && song.File.DiffDrums.Value < Ratings.Count)
                                {
                                    songView.DrumString = Ratings[(int)song.File.DiffDrums.Value];
                                }
                                else
                                {
                                    songView.DrumString = Ratings[0];
                                }

                                if (song.File.DiffGuitar.HasValue && song.File.DiffGuitar.Value >= 0 && song.File.DiffGuitar.Value < Ratings.Count)
                                {
                                    songView.GuitarString = Ratings[(int)song.File.DiffGuitar.Value];
                                }
                                else
                                {
                                    songView.GuitarString = Ratings[0];
                                }

                                if (song.File.DiffBass.HasValue && song.File.DiffBass.Value >= 0 && song.File.DiffBass.Value < Ratings.Count)
                                {
                                    songView.BassString = Ratings[(int)song.File.DiffBass.Value];
                                }
                                else
                                {
                                    songView.BassString = Ratings[0];
                                }

                                if (song.File.DiffVocals.HasValue && song.File.DiffVocals.Value >= 0 && song.File.DiffVocals.Value < Ratings.Count)
                                {
                                    songView.VocalString = Ratings[(int)song.File.DiffVocals.Value];
                                }
                                else
                                {
                                    songView.VocalString = Ratings[0];
                                }

                                if (dictionary.TryGetValue(song.File.Gameformat, out List<string>? value))
                                {
                                    songView.Gameformat = value[1];
                                }

                                if (!DataItems.Contains(songView))
                                {
                                    DataItems.Add(songView);
                                }
                            }
                        }

                    }
                    else
                    {
                        HasMoreRecords = false;
                    }
                    return DataItems;
                }
                
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Request error: {e.Message}");
                    return [];
                }
            }

               
            catch (Exception ex)
            {
                // Handle exceptions
                Logger.LogMessage($"An error occurred: {ex.Message}");
                return [];
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}