using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhythmVerseClient.DTO
{/*

    public class Rootobject
    {
        public string status { get; set; }
        public Data data { get; set; }
    }

    public class Data
    {
        public Records records { get; set; }
        public Pagination pagination { get; set; }
        public Song[] songs { get; set; }
    }

    public class Records
    {
        public int total_available { get; set; }
        public int total_filtered { get; set; }
        public int returned { get; set; }
    }

    public class Pagination
    {
        public int start { get; set; }
        public string records { get; set; }
        public string page { get; set; }
    }

    public class Song
    {
        public Data1 data { get; set; }
        public File file { get; set; }
    }

    public class Data1
    {
        public int song_id { get; set; }
        public int member_id { get; set; }
        public int record_saved { get; set; }
        public int record_updated { get; set; }
        public int record_locked { get; set; }
        public int record_comments { get; set; }
        public int record_views { get; set; }
        public int song_length { get; set; }
        public string genre { get; set; }
        public string subgenre { get; set; }
        public int year { get; set; }
        public string album { get; set; }
        public string album_s { get; set; }
        public int? album_track_number { get; set; }
        public int decade { get; set; }
        public string artist { get; set; }
        public string artist_s { get; set; }
        public int artist_id { get; set; }
        public string title { get; set; }
        public string diff_drums { get; set; }
        public string diff_guitar { get; set; }
        public string diff_bass { get; set; }
        public string diff_vocals { get; set; }
        public string diff_keys { get; set; }
        public string diff_prokeys { get; set; }
        public string diff_proguitar { get; set; }
        public string diff_probass { get; set; }
        public string diff_band { get; set; }
        public int master { get; set; }
        public string vocal_parts { get; set; }
        public string gender { get; set; }
        public string rating { get; set; }
        public string song_preview { get; set; }
        public string song_notes { get; set; }
        public int downloads { get; set; }
        public int record_approved { get; set; }
        public string diff_rhythm { get; set; }
        public string diff_guitar_coop { get; set; }
        public object diff_drums_real_ps { get; set; }
        public object diff_keys_real_ps { get; set; }
        public object diff_dance { get; set; }
        public object diff_vocals_harm { get; set; }
        public string diff_guitarghl { get; set; }
        public string diff_bassghl { get; set; }
        public int genre_is_literal { get; set; }
        public object rank_drums { get; set; }
        public object rank_guitar { get; set; }
        public object rank_bass { get; set; }
        public object rank_vocals { get; set; }
        public object rank_keys { get; set; }
        public object rank_prokeys { get; set; }
        public object rank_probass { get; set; }
        public object rank_proguitar { get; set; }
        public object rank_guitar_coop { get; set; }
        public object rank_band { get; set; }
        public string album_art { get; set; }
        public Tiers tiers { get; set; }
        public string genre_id { get; set; }
        public string record_id { get; set; }
    }

    public class Tiers
    {
        public int songstiers_id { get; set; }
        public int song_id { get; set; }
        public string gameformat { get; set; }
        public string diff_drums { get; set; }
        public string diff_guitar { get; set; }
        public string diff_bass { get; set; }
        public string diff_vocals { get; set; }
        public string diff_keys { get; set; }
        public string diff_prokeys { get; set; }
        public string diff_proguitar { get; set; }
        public string diff_probass { get; set; }
        public object diff_band { get; set; }
        public object diff_dance { get; set; }
        public string diff_bassghl { get; set; }
        public string diff_guitarghl { get; set; }
        public object diff_keys_real_ps { get; set; }
        public object diff_drums_real_ps { get; set; }
        public object diff_vocals_harm { get; set; }
        public string diff_guitar_coop { get; set; }
        public string diff_rhythm { get; set; }
        public object rank_drums { get; set; }
        public object rank_guitar { get; set; }
        public object rank_bass { get; set; }
        public object rank_vocals { get; set; }
        public object rank_keys { get; set; }
        public object rank_prokeys { get; set; }
        public object rank_probass { get; set; }
        public object rank_proguitar { get; set; }
        public object rank_guitar_coop { get; set; }
        public object rank_band { get; set; }
    }

    public class File
    {
        public int diff_drums { get; set; }
        public int diff_guitar { get; set; }
        public int diff_bass { get; set; }
        public int diff_vocals { get; set; }
        public string file_id { get; set; }
        public int db_id { get; set; }
        public string user { get; set; }
        public string user_folder { get; set; }
        public string file_name { get; set; }
        public string giorno { get; set; }
        public string gameformat { get; set; }
        public string gamesource { get; set; }
        public object source { get; set; }
        public object group_id { get; set; }
        public object alt_versions { get; set; }
        public int downloads { get; set; }
        public int deleted { get; set; }
        public int retired { get; set; }
        public int destroyed { get; set; }
        public int size { get; set; }
        public int utility { get; set; }
        public int unpitched { get; set; }
        public string audio_type { get; set; }
        public string tuning_offset_cents { get; set; }
        public string encoding { get; set; }
        public string has_reductions { get; set; }
        public string vocal_parts_authored { get; set; }
        public string file_preview { get; set; }
        public string file_notes { get; set; }
        public string custom_id { get; set; }
        public string external_url { get; set; }
        public string disc { get; set; }
        public int? completeness { get; set; }
        public object wip { get; set; }
        public string wip_date { get; set; }
        public int record_hidden { get; set; }
        public string release_date { get; set; }
        public object future_release_date { get; set; }
        public object delete_date { get; set; }
        public object retire_date { get; set; }
        public int pro_drums { get; set; }
        public int vocals_lyrics_only { get; set; }
        public object charter { get; set; }
        public string record_updated { get; set; }
        public string file_updated { get; set; }
        public string record_created { get; set; }
        public string file_artist { get; set; }
        public string file_artist_s { get; set; }
        public string file_title { get; set; }
        public string file_album { get; set; }
        public string file_album_s { get; set; }
        public string file_genre { get; set; }
        public object file_subgenre { get; set; }
        public int file_genre_is_literal { get; set; }
        public int file_year { get; set; }
        public int file_decade { get; set; }
        public int file_song_length { get; set; }
        public int? file_album_track_number { get; set; }
        public string filename { get; set; }
        public string upload_date { get; set; }
        public int off { get; set; }
        public Author author { get; set; }
        public bool hidden { get; set; }
        public int game_completeness { get; set; }
        public string file_url { get; set; }
        public string file_url_full { get; set; }
        public string author_id { get; set; }
        public int comments { get; set; }
        public object update_date { get; set; }
        public string album_art { get; set; }
        public string file_genre_id { get; set; }
        public Difficulties difficulties { get; set; }
        public object credits { get; set; }
        public int thanks { get; set; }
        public string download_url { get; set; }
        public string download_page_url { get; set; }
        public string download_page_url_full { get; set; }
        public object group { get; set; }
        public int song_length { get; set; }
    }

    public class Author
    {
        public int member_id { get; set; }
        public string name { get; set; }
        public string account { get; set; }
        public int releases { get; set; }
        public string default_gameformat { get; set; }
        public string shortname { get; set; }
        public string role { get; set; }
        public string author_class { get; set; }
        public object level { get; set; }
        public int confirmed { get; set; }
        public int source { get; set; }
        public int? id { get; set; }
        public int dl_count { get; set; }
        public string public_profile_page { get; set; }
        public string songlist_url { get; set; }
        public string author_url { get; set; }
        public string author_url_full { get; set; }
        public string avatar_path { get; set; }
    }

    public class Difficulties
    {
        public object drums { get; set; }
        public Guitar guitar { get; set; }
        public Bass bass { get; set; }
        public Vocals vocals { get; set; }
    }

    public class Guitar
    {
        public int x { get; set; }
        public int e { get; set; }
        public int m { get; set; }
        public int h { get; set; }
        public int all { get; set; }
    }

    public class Bass
    {
        public int x { get; set; }
        public int e { get; set; }
        public int m { get; set; }
        public int h { get; set; }
        public int all { get; set; }
    }

    public class Vocals
    {
        public int e { get; set; }
        public int m { get; set; }
        public int h { get; set; }
        public int x { get; set; }
        public int all { get; set; }
    }
*/
}
