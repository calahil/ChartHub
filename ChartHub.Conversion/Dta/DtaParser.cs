using System.Text;

namespace ChartHub.Conversion.Dta;

/// <summary>
/// Parsed data from a Rock Band songs.dta file.
/// </summary>
internal sealed class DtaSongInfo
{
    public required string ShortName { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string Charter { get; init; } = "Unknown";
    public string SongFilePath { get; init; } = string.Empty;

    /// <summary>Channel assignments per instrument stem (drum, bass, guitar, vocals, keys, crowd).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> TrackChannels { get; init; }
        = new Dictionary<string, IReadOnlyList<int>>();

    /// <summary>Pan values for each channel in the MOGG file.</summary>
    public IReadOnlyList<float> Pans { get; init; } = [];

    /// <summary>Volume values (dB) for each channel in the MOGG file.</summary>
    public IReadOnlyList<float> Vols { get; init; } = [];

    /// <summary>Total number of MOGG channels (derived from tracks_count array).</summary>
    public int TotalChannels { get; init; }
}

/// <summary>
/// Minimal parser for the Lisp-style S-expression format used by Rock Band songs.dta files.
/// </summary>
internal static class DtaParser
{
    /// <summary>Parses a songs.dta byte array and returns the first song entry found.</summary>
    public static DtaSongInfo Parse(byte[] dtaBytes)
    {
        return ParseAll(dtaBytes).First();
    }

    /// <summary>Parses a songs.dta byte array and returns every song entry found.</summary>
    public static IReadOnlyList<DtaSongInfo> ParseAll(byte[] dtaBytes)
    {
        string text = Encoding.Latin1.GetString(dtaBytes);
        List<string> tokens = Tokenise(text);
        int pos = 0;
        DtaNode root = ParseList(tokens, ref pos);
        List<DtaNode> songNodes = FindSongNodes(root);
        if (songNodes.Count == 0)
        {
            throw new InvalidDataException("DTA file contains no parseable song entry.");
        }

        return songNodes.Select(ExtractSong).ToList();
    }

    // ---- tokeniser ---------------------------------------------------------

    private static List<string> Tokenise(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Skip whitespace
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Line comment: ; or //
            if (c == ';' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '(' || c == ')') { tokens.Add(c.ToString()); i++; continue; }

            // Quoted string
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++; // skip opening quote
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { i++; }
                    sb.Append(text[i]);
                    i++;
                }

                i++; // skip closing quote
                tokens.Add('"' + sb.ToString() + '"');
                continue;
            }

            // Atom: read until whitespace or paren
            {
                var sb = new StringBuilder();
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')')
                {
                    sb.Append(text[i]);
                    i++;
                }

                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                }
            }
        }

        return tokens;
    }

    // ---- recursive descent -------------------------------------------------

    private sealed class DtaNode
    {
        public string? Atom { get; init; }
        public List<DtaNode>? Children { get; init; }
        public bool IsList => Children != null;
    }

    private static DtaNode ParseNode(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
        {
            return new DtaNode { Atom = "" };
        }

        if (tokens[pos] == "(")
        {
            return ParseList(tokens, ref pos);
        }

        return new DtaNode { Atom = tokens[pos++] };
    }

    private static DtaNode ParseList(List<string> tokens, ref int pos)
    {
        var children = new List<DtaNode>();

        // consume '('
        if (pos < tokens.Count && tokens[pos] == "(")
        {
            pos++;
        }

        while (pos < tokens.Count && tokens[pos] != ")")
        {
            children.Add(ParseNode(tokens, ref pos));
        }

        if (pos < tokens.Count)
        {
            pos++; // consume ')'
        }

        return new DtaNode { Children = children };
    }

    // ---- extraction --------------------------------------------------------

    private static List<DtaNode> FindSongNodes(DtaNode root)
    {
        // Determine the song entry node:
        // - If root's first child is an atom, root itself IS the song entry (no outer wrapper).
        // - If root's first child is a list, root is a container of song entries.
        List<DtaNode> songNodes = [];
        if (root.IsList && root.Children!.Count > 0)
        {
            if (root.Children[0].Atom != null)
            {
                songNodes.Add(root);
            }
            else
            {
                foreach (DtaNode child in root.Children!)
                {
                    if (child.IsList && child.Children!.Count > 0 && child.Children[0].Atom != null)
                    {
                        songNodes.Add(child);
                    }
                }
            }
        }

        return songNodes;
    }

    private static DtaSongInfo ExtractSong(DtaNode songNode)
    {
        if (!songNode.IsList || songNode.Children!.Count == 0)
        {
            throw new InvalidDataException("DTA file contains no parseable song entry.");
        }

        List<DtaNode> children = songNode.Children!;

        // First child is the song short name (possibly quoted with ')
        string shortName = children[0].Atom?.Trim('\'') ?? "unknown";

        string title = string.Empty;
        string artist = string.Empty;
        string charter = "Unknown";
        string songFilePath = string.Empty;
        var trackChannels = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        var pans = new List<float>();
        var vols = new List<float>();
        int totalChannels = 0;

        for (int i = 1; i < children.Count; i++)
        {
            DtaNode entry = children[i];
            if (!entry.IsList || entry.Children!.Count < 2)
            {
                continue;
            }

            string key = entry.Children![0].Atom?.Trim('\'') ?? string.Empty;
            DtaNode value = entry.Children![1];

            switch (key)
            {
                case "name":
                    title = UnquoteString(ExtractFirstAtom(value));
                    break;

                case "artist":
                    artist = UnquoteString(ExtractFirstAtom(value));
                    break;

                case "charter":
                case "author":
                    charter = UnquoteString(ExtractFirstAtom(value));
                    break;

                case "song":
                    ExtractSongBlock(entry.Children!, ref songFilePath, ref totalChannels, trackChannels, pans, vols);
                    break;
            }
        }

        return new DtaSongInfo
        {
            ShortName = shortName,
            Title = title,
            Artist = artist,
            Charter = charter,
            SongFilePath = songFilePath,
            TrackChannels = trackChannels,
            Pans = pans,
            Vols = vols,
            TotalChannels = totalChannels,
        };
    }

    private static void ExtractSongBlock(
        List<DtaNode> songBlockChildren,
        ref string songFilePath,
        ref int totalChannels,
        Dictionary<string, IReadOnlyList<int>> trackChannels,
        List<float> pans,
        List<float> vols)
    {
        for (int i = 1; i < songBlockChildren.Count; i++)
        {
            DtaNode entry = songBlockChildren[i];
            if (!entry.IsList || entry.Children!.Count < 2)
            {
                continue;
            }

            string key = entry.Children![0].Atom?.Trim('\'') ?? string.Empty;

            switch (key)
            {
                case "name":
                    songFilePath = UnquoteString(entry.Children![1].Atom ?? string.Empty);
                    break;

                case "tracks_count":
                    // (tracks_count (N N N N N N)) — counts per instrument
                    if (entry.Children![1].IsList)
                    {
                        totalChannels = entry.Children![1].Children!
                            .Sum(n => ParseInt(n.Atom));
                    }

                    break;

                case "tracks":
                    ExtractTracksBlock(entry.Children![1], trackChannels);
                    break;

                case "pans":
                    // (pans (f f f ...))
                    if (entry.Children![1].IsList)
                    {
                        foreach (DtaNode n in entry.Children![1].Children!)
                        {
                            pans.Add(ParseFloat(n.Atom));
                        }
                    }

                    break;

                case "vols":
                    if (entry.Children![1].IsList)
                    {
                        foreach (DtaNode n in entry.Children![1].Children!)
                        {
                            vols.Add(ParseFloat(n.Atom));
                        }
                    }

                    break;
            }
        }
    }

    private static void ExtractTracksBlock(DtaNode tracksNode, Dictionary<string, IReadOnlyList<int>> trackChannels)
    {
        if (!tracksNode.IsList)
        {
            return;
        }

        foreach (DtaNode item in tracksNode.Children!)
        {
            if (!item.IsList || item.Children!.Count < 2)
            {
                continue;
            }

            string instrument = item.Children![0].Atom?.Trim('\'') ?? string.Empty;
            if (string.IsNullOrEmpty(instrument))
            {
                continue;
            }

            DtaNode channelList = item.Children![1];
            if (!channelList.IsList)
            {
                continue;
            }

            var channels = channelList.Children!
                .Select(n => ParseInt(n.Atom))
                .ToList();

            trackChannels[instrument] = channels;
        }
    }

    private static string UnquoteString(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            return s[1..^1];
        }

        return s;
    }

    private static string ExtractFirstAtom(DtaNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Atom))
        {
            return node.Atom;
        }

        if (!node.IsList || node.Children is null)
        {
            return string.Empty;
        }

        int startIndex = 0;
        if (node.Children.Count > 1 && !string.IsNullOrWhiteSpace(node.Children[0].Atom))
        {
            // Treat list as a key/value tuple and prefer value nodes over the key atom.
            startIndex = 1;
        }

        for (int i = startIndex; i < node.Children.Count; i++)
        {
            string value = ExtractFirstAtom(node.Children[i]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static int ParseInt(string? s)
    {
        if (s != null && int.TryParse(s, out int v))
        {
            return v;
        }

        return 0;
    }

    private static float ParseFloat(string? s)
    {
        if (s != null && float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v))
        {
            return v;
        }

        return 0f;
    }
}
