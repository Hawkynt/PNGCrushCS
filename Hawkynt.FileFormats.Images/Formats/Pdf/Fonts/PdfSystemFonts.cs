using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFormat.Pdf.Fonts;

/// <summary>
/// The installed fonts a non-embedded CJK CID font can be drawn with: TrueType and OpenType files
/// and collections (.ttf, .otf, .ttc, .otc) in the platform's font folders and any the caller adds.
/// </summary>
/// <remarks>
/// The folders are those the platforms document: on Windows <c>%WINDIR%\Fonts</c> and the per-user
/// <c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>; on macOS <c>/System/Library/Fonts</c>,
/// <c>/Library/Fonts</c> and <c>~/Library/Fonts</c>; elsewhere the XDG data folders' <c>fonts</c>
/// (<c>~/.local/share/fonts</c>, <c>/usr/local/share/fonts</c>, <c>/usr/share/fonts</c>), <c>~/.fonts</c>
/// and the <c>&lt;dir&gt;</c> entries of fontconfig's main configuration file (<c>$FONTCONFIG_FILE</c>
/// or <c>/etc/fonts/fonts.conf</c>, and <c>/etc/fonts/local.conf</c>), read as text — fontconfig
/// itself is not used. Folders are searched with their subfolders.
/// <para/>
/// Nothing is read until a document needs a substitute. Then each file's table directory, name and
/// OS/2 tables are read, and for a font that may be CJK at all (one with a CJK code page bit or a
/// cmap large enough to hold one) the Unicode cmap, to see which of the four collections it can
/// draw: a font covers Adobe-Japan1 when it maps あ, ア and 漢, Adobe-GB1 for 中, 国 and 这,
/// Adobe-CNS1 for 中, 國 and 這, and Adobe-Korea1 for 가 and 한. The result is kept for the life of
/// the process, per set of folders; a file that cannot be read, or that is not a font, is passed
/// over. A face is loaded in full only when it is chosen, and kept while it is in use.
/// <para/>
/// A face is chosen for a collection by its region first — the JP, KR, SC, TC and similar suffix of
/// a pan-CJK family, or the region a well-known family belongs to — so Chinese text gets Chinese
/// glyph forms, then by an installed family of the PDF font's own name, then by serif (Mincho, Song,
/// Ming, Batang) against sans (Gothic, Hei, Gulim) as the PDF font's name or flags ask, then weight,
/// and upright and proportional faces over italic and monospaced ones.
/// </remarks>
internal sealed class PdfSystemFonts {

  private const int _MaxFiles = 50_000;
  private const int _MaxFaces = 64;
  private const long _MaxFileBytes = 1L << 30;
  private const int _MaxTableBytes = 16 << 20;

  private static readonly string[] _Extensions = [".ttf", ".otf", ".ttc", ".otc"];
  private static readonly ConcurrentDictionary<string, Lazy<Face[]>> _Indexes = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, Face[] Faces)> _Files = new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<(string Path, int Index), WeakReference<PdfSystemFace>> _Loaded = new();
  private static readonly ConcurrentDictionary<(string Path, int Index), bool> _Unloadable = new();
  private static readonly ConcurrentDictionary<string, Lazy<PdfSystemFonts>> _Sets = new(StringComparer.Ordinal);

  private readonly string[] _directories;
  private readonly Lazy<Face[]> _faces;

  private PdfSystemFonts(string[] directories) {
    this._directories = directories;
    var key = string.Join('\n', directories);
    this._faces = _Indexes.GetOrAdd(key, _ => new Lazy<Face[]>(() => _Scan(directories)));
  }

  /// <summary>The folders searched, in order.</summary>
  public IReadOnlyList<string> Directories => this._directories;

  /// <summary>Every face found, for diagnostics and tests.</summary>
  internal IReadOnlyList<Face> Faces => this._faces.Value;

  /// <summary>
  /// The fonts to search for a set of options: the caller's folders first, then the platform's
  /// unless system fonts are turned off; null when there is nowhere to look.
  /// </summary>
  public static PdfSystemFonts? For(IReadOnlyList<string>? extra, bool useSystemFonts) {
    var directories = new List<string>();
    if (extra != null)
      foreach (var directory in extra)
        if (!string.IsNullOrWhiteSpace(directory))
          directories.Add(_Full(directory));
    if (useSystemFonts)
      directories.AddRange(_PlatformDirectories.Value);

    var distinct = directories.Distinct(StringComparer.Ordinal).ToArray();
    if (distinct.Length == 0)
      return null;

    var key = string.Join('\n', distinct);
    return _Sets.GetOrAdd(key, _ => new Lazy<PdfSystemFonts>(() => new PdfSystemFonts(distinct))).Value;
  }

  private static readonly Lazy<IReadOnlyList<string>> _PlatformDirectories = new(PlatformDirectories);

  /// <summary>The folders fonts are installed in on this platform.</summary>
  public static IReadOnlyList<string> PlatformDirectories() {
    var list = new List<string>();
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (OperatingSystem.IsWindows()) {
      var windows = Environment.GetEnvironmentVariable("WINDIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
      if (!string.IsNullOrEmpty(windows))
        list.Add(Path.Combine(windows, "Fonts"));
      var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      if (!string.IsNullOrEmpty(local))
        list.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
    } else if (OperatingSystem.IsMacOS()) {
      list.Add("/System/Library/Fonts");
      list.Add("/Library/Fonts");
      if (!string.IsNullOrEmpty(home))
        list.Add(Path.Combine(home, "Library", "Fonts"));
      list.Add("/Network/Library/Fonts");
    } else {
      var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
      if (string.IsNullOrEmpty(dataHome) && !string.IsNullOrEmpty(home))
        dataHome = Path.Combine(home, ".local", "share");
      if (!string.IsNullOrEmpty(dataHome))
        list.Add(Path.Combine(dataHome, "fonts"));
      if (!string.IsNullOrEmpty(home))
        list.Add(Path.Combine(home, ".fonts"));
      var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
      foreach (var dir in (string.IsNullOrEmpty(dataDirs) ? "/usr/local/share:/usr/share" : dataDirs).Split(':', StringSplitOptions.RemoveEmptyEntries))
        list.Add(Path.Combine(dir, "fonts"));
      foreach (var config in new[] { Environment.GetEnvironmentVariable("FONTCONFIG_FILE"), "/etc/fonts/fonts.conf", "/etc/fonts/local.conf" })
        if (!string.IsNullOrEmpty(config))
          list.AddRange(FontconfigDirectories(config, home, dataHome));
    }

    return list.Select(_Full).Distinct(StringComparer.Ordinal).ToArray();
  }

  /// <summary>The <c>&lt;dir&gt;</c> entries of a fontconfig configuration file, with their prefixes resolved.</summary>
  internal static IEnumerable<string> FontconfigDirectories(string file, string? home, string? dataHome) {
    string text;
    try {
      var info = new FileInfo(file);
      if (!info.Exists || info.Length > 1 << 20)
        return [];
      text = File.ReadAllText(file);
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
      return [];
    }

    var result = new List<string>();
    var configDirectory = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "/";
    foreach (Match match in Regex.Matches(text, @"<dir(\s[^>]*)?>\s*([^<]+?)\s*</dir>")) {
      var attributes = match.Groups[1].Value;
      var path = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
      if (attributes.Contains("prefix=\"xdg\"", StringComparison.Ordinal) && dataHome != null)
        path = Path.Combine(dataHome, path);
      else if (attributes.Contains("prefix=\"relative\"", StringComparison.Ordinal) && !Path.IsPathRooted(path))
        path = Path.Combine(configDirectory, path);
      else if (path.StartsWith('~') && home != null)
        path = home + path[1..];
      if (Path.IsPathRooted(path))
        result.Add(path);
    }

    return result;
  }

  /// <summary>
  /// The face to draw a non-embedded CID font of an Adobe collection with, loaded, or null when no
  /// installed font covers the collection.
  /// </summary>
  public PdfSystemFace? Find(string ordering, string pdfFontName, bool serif, bool bold, bool fixedPitch) {
    var want = PdfFontNames.Normalize(pdfFontName);
    var ranked = this._faces.Value
      .Where(face => face.Covers(ordering))
      .Select(face => (Face: face, Score: face.Score(ordering, want, serif, bold, fixedPitch)))
      .OrderByDescending(entry => entry.Score)
      .ThenBy(entry => entry.Face.Path, StringComparer.Ordinal)
      .ThenBy(entry => entry.Face.Index);

    foreach (var (face, _) in ranked)
      if (_Load(face) is { } loaded)
        return loaded;

    return null;
  }

  private static PdfSystemFace? _Load(Face face) {
    var key = (face.Path, face.Index);
    if (_Unloadable.ContainsKey(key))
      return null;
    if (_Loaded.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached))
      return cached;

    var loaded = PdfSystemFace.Load(face);
    if (loaded != null)
      _Loaded[key] = new WeakReference<PdfSystemFace>(loaded);
    else
      _Unloadable[key] = true;
    return loaded;
  }

  private static string _Full(string path) {
    try {
      return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is { Length: > 0 } full ? full : path;
    } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException) {
      return path;
    }
  }

  #region scanning

  private static Face[] _Scan(string[] directories) {
    var faces = new List<Face>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var options = new EnumerationOptions {
      RecurseSubdirectories = true,
      IgnoreInaccessible = true,
      MaxRecursionDepth = 8,
      AttributesToSkip = FileAttributes.System,
      ReturnSpecialDirectories = false,
    };

    foreach (var directory in directories) {
      IEnumerable<string> files;
      try {
        if (!Directory.Exists(directory))
          continue;
        files = Directory.EnumerateFiles(directory, "*", options);
      } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) {
        continue;
      }

      try {
        foreach (var file in files) {
          if (seen.Count >= _MaxFiles)
            break;
          if (!_Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) || !seen.Add(file))
            continue;
          faces.AddRange(_FacesOf(file));
        }
      } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
        // A folder that goes away while it is walked ends that folder's walk.
      }
    }

    return faces.ToArray();
  }

  private static Face[] _FacesOf(string file) {
    try {
      var info = new FileInfo(file);
      if (!info.Exists || info.Length < 12 || info.Length > _MaxFileBytes)
        return [];
      if (_Files.TryGetValue(file, out var known) && known.Length == info.Length && known.Written == info.LastWriteTimeUtc)
        return known.Faces;

      Face[] faces;
      using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess))
        faces = _Read(file, stream);
      _Files[file] = (info.Length, info.LastWriteTimeUtc, faces);
      return faces;
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                  or InvalidDataException or IndexOutOfRangeException or OverflowException or System.Security.SecurityException) {
      return [];
    }
  }

  private static Face[] _Read(string file, FileStream stream) {
    var header = _ReadAt(stream, 0, 12);
    var offsets = new List<long>();
    if (header[0] == 't' && header[1] == 't' && header[2] == 'c' && header[3] == 'f') {
      var count = (int)Math.Min(Math.Min(_U32(header, 8), _MaxFaces), (stream.Length - 12) / 4);
      var table = _ReadAt(stream, 12, count * 4);
      for (var i = 0; i < count; ++i)
        offsets.Add(_U32(table, i * 4));
    } else
      offsets.Add(0);

    // A face that does not add up is passed over; the others in the collection still count.
    var faces = new List<Face>();
    var cmapCoverage = new Dictionary<long, int>();
    for (var index = 0; index < offsets.Count; ++index)
      try {
        if (_ReadFace(file, index, stream, offsets[index], cmapCoverage) is { } face)
          faces.Add(face);
      } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or OverflowException or EndOfStreamException) {
      }

    return faces.ToArray();
  }

  private static Face? _ReadFace(string file, int index, FileStream stream, long at, Dictionary<long, int> cmapCoverage) {
    var header = _ReadAt(stream, at, 12);
    var version = _U32(header, 0);
    if (version is not (0x00010000 or 0x4F54544F or 0x74727565))
      return null;

    var count = _U16(header, 4);
    var directory = _ReadAt(stream, at + 12, count * 16);
    var tables = new Dictionary<string, (long Offset, long Length)>(StringComparer.Ordinal);
    for (var i = 0; i < count; ++i) {
      var tag = Encoding.ASCII.GetString(directory, i * 16, 4);
      tables.TryAdd(tag, (_U32(directory, i * 16 + 8), _U32(directory, i * 16 + 12)));
    }

    // Outlines this renderer draws: glyf, or CFF (not CFF2, not bitmaps alone).
    if (!tables.ContainsKey("glyf") && !tables.ContainsKey("CFF "))
      return null;
    if (!tables.TryGetValue("cmap", out var cmap) || cmap.Length < 4)
      return null;

    var names = tables.TryGetValue("name", out var name) ? _Names(_ReadTable(stream, name)) : new List<(int Id, string Text, bool English)>();
    var family = names.FirstOrDefault(n => n.Id == 16 && n.English).Text ?? names.FirstOrDefault(n => n.Id == 1 && n.English).Text
                 ?? names.FirstOrDefault(n => n.Id == 1).Text ?? Path.GetFileNameWithoutExtension(file);
    var subfamily = names.FirstOrDefault(n => n.Id == 17 && n.English).Text ?? names.FirstOrDefault(n => n.Id == 2 && n.English).Text ?? "";
    var postScript = names.FirstOrDefault(n => n.Id == 6).Text ?? "";
    var allFamilies = names.Where(n => n.Id is 1 or 4 or 6 or 16).Select(n => PdfFontNames.Normalize(n.Text)).Where(n => n.Length > 0).Distinct().ToArray();

    var weight = 400;
    var italic = subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase) || subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
    uint codePages = 0;
    var serifStyle = 0;
    if (tables.TryGetValue("OS/2", out var os2) && os2.Length >= 78) {
      var data = _ReadAt(stream, os2.Offset, (int)Math.Min(os2.Length, 96));
      weight = _U16(data, 4);
      serifStyle = data[32 + 1];
      italic |= (_U16(data, 62) & 1) != 0;
      if (data.Length >= 82 && _U16(data, 0) >= 1)
        codePages = _U32(data, 78);
    }

    // The Unicode cmap is read only for a font that may cover CJK; a collection's faces usually share one.
    var coverage = 0;
    const uint cjkCodePages = (1u << 17) | (1u << 18) | (1u << 19) | (1u << 20) | (1u << 21);
    if ((codePages & cjkCodePages) != 0 || cmap.Length >= 8192 || PdfFontNames.Region(family) != null) {
      var subtable = UnicodeSubtable(_ReadAt(stream, cmap.Offset, (int)Math.Min(cmap.Length, 8 + 8 * 64)), cmap.Offset);
      if (subtable >= 0 && !cmapCoverage.TryGetValue(subtable, out coverage)) {
        coverage = _Coverage(stream, subtable);
        cmapCoverage[subtable] = coverage;
      }
    }

    if (coverage == 0)
      return null;

    return new Face(file, index, family, subfamily, postScript, allFamilies, weight, italic, serifStyle, codePages, coverage);
  }

  /// <summary>The offset of the cmap subtable poppler would take: (3,10), else (3,1), else the first Unicode-platform one.</summary>
  internal static long UnicodeSubtable(byte[] cmap, long cmapOffset) {
    var count = Math.Min(_U16(cmap, 2), (cmap.Length - 4) / 8);
    long chosen = -1;
    var rank = 0;
    for (var i = 0; i < count; ++i) {
      var record = 4 + i * 8;
      var platform = _U16(cmap, record);
      var encoding = _U16(cmap, record + 2);
      var offset = cmapOffset + _U32(cmap, record + 4);
      var r = platform == 3 && encoding == 10 ? 3 : platform == 3 && encoding == 1 ? 2 : platform == 0 ? 1 : 0;
      if (r > rank) {
        rank = r;
        chosen = offset;
      }
    }

    return chosen;
  }

  private static readonly (string Ordering, int[] Characters)[] _Probes = [
    ("Japan1", [0x3042, 0x30A2, 0x6F22]),
    ("GB1", [0x4E2D, 0x56FD, 0x8FD9]),
    ("CNS1", [0x4E2D, 0x570B, 0x9019]),
    ("Korea1", [0xAC00, 0xD55C]),
  ];

  private static int _Coverage(FileStream stream, long subtable) {
    var head = _ReadAt(stream, subtable, 8);
    var format = _U16(head, 0);
    long length = format switch {
      4 or 0 or 6 => _U16(head, 2),
      12 or 13 => _U32(head, 4),
      _ => 0,
    };
    if (length < 8 || length > _MaxTableBytes)
      return 0;

    var cmap = SfntCmap.Read(_ReadAt(stream, subtable, (int)length), 0);
    if (cmap == null)
      return 0;

    var coverage = 0;
    for (var i = 0; i < _Probes.Length; ++i)
      if (_Probes[i].Characters.All(c => cmap.Lookup(c) > 0))
        coverage |= 1 << i;
    return coverage;
  }

  private static List<(int Id, string Text, bool English)> _Names(byte[] data) {
    var names = new List<(int, string, bool)>();
    if (data.Length < 6)
      return names;

    var count = _U16(data, 2);
    var storage = _U16(data, 4);
    for (var i = 0; i < count; ++i) {
      var record = 6 + i * 12;
      if (record + 12 > data.Length)
        break;

      var platform = _U16(data, record);
      var encoding = _U16(data, record + 2);
      var language = _U16(data, record + 4);
      var id = _U16(data, record + 6);
      var length = _U16(data, record + 8);
      var offset = storage + _U16(data, record + 10);
      if (id is not (1 or 2 or 4 or 6 or 16 or 17) || offset + length > data.Length)
        continue;

      string text;
      if (platform == 3 || platform == 0)
        text = Encoding.BigEndianUnicode.GetString(data, offset, length & ~1);
      else if (platform == 1 && encoding == 0)
        text = Encoding.Latin1.GetString(data, offset, length);
      else
        continue;

      var english = platform == 3 ? language == 0x409 : platform == 1 ? language == 0 : true;
      if (text.Length > 0)
        names.Add((id, text.Trim('\0', ' '), english));
    }

    // English names first, so a lookup by id finds them before the localised ones.
    return names.OrderByDescending(n => n.Item3).ToList();
  }

  private static byte[] _ReadTable(FileStream stream, (long Offset, long Length) table)
    => _ReadAt(stream, table.Offset, (int)Math.Min(table.Length, _MaxTableBytes));

  private static byte[] _ReadAt(FileStream stream, long offset, int length) {
    if (offset < 0 || length < 0 || offset + length > stream.Length)
      throw new InvalidDataException("A font table reaches past the end of the file.");

    var buffer = new byte[length];
    stream.Position = offset;
    stream.ReadExactly(buffer);
    return buffer;
  }

  private static ushort _U16(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);

  private static uint _U32(byte[] data, int at) => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);

  #endregion

  /// <summary>One face of an installed font file, as far as choosing it needs.</summary>
  internal sealed record Face(
    string Path,
    int Index,
    string Family,
    string Subfamily,
    string PostScriptName,
    string[] NormalizedNames,
    int Weight,
    bool Italic,
    int PanoseSerifStyle,
    uint CodePages,
    int Coverage) {

    /// <summary>The collection a pan-CJK or regional family is made for, from its names, or null.</summary>
    public string? Region => PdfFontNames.Region(this.Family) ?? PdfFontNames.Region(this.PostScriptName);

    /// <summary>Whether the face's design has serifs: its name first, the PANOSE serif style after.</summary>
    public bool? IsSerif => PdfFontNames.IsSerif(this.Family) ?? PdfFontNames.IsSerif(this.PostScriptName)
                            ?? (this.PanoseSerifStyle is >= 2 and <= 10 ? true : this.PanoseSerifStyle is >= 11 and <= 13 ? false : null);

    /// <summary>Whether it is a monospaced design.</summary>
    public bool IsMonospaced => this.Family.Contains("Mono", StringComparison.OrdinalIgnoreCase) || this.Family.Contains("Gothic Coding", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether its characters cover a collection.</summary>
    public bool Covers(string ordering) {
      var bit = Array.FindIndex(_Probes, p => p.Ordering == ordering);
      return bit >= 0 && (this.Coverage & (1 << bit)) != 0;
    }

    /// <summary>How well the face suits a request; higher is better.</summary>
    public int Score(string ordering, string wantedName, bool serif, bool bold, bool fixedPitch) {
      var score = 0;
      if (wantedName.Length > 0 && this.NormalizedNames.Contains(wantedName))
        score += 100;

      var region = this.Region;
      if (region == ordering)
        score += 40;
      else if (region != null)
        score -= 40;
      else if ((this.CodePages & PdfFontNames.CodePageBits(ordering)) != 0)
        score += 10;

      score += this.IsSerif switch {
        { } s when s == serif => 20,
        null => 5,
        _ => 0,
      };

      score -= Math.Abs(this.Weight - (bold ? 700 : 400)) / 100 * 3;
      if (this.Italic)
        score -= 15;
      score += this.IsMonospaced == fixedPitch ? 0 : -5;
      if (Regex.IsMatch(this.Family, @"\bUI\b"))
        score -= 2;
      return score;
    }
  }
}

/// <summary>An installed face loaded for drawing: its outlines, Unicode cmap and vertical alternates.</summary>
internal sealed class PdfSystemFace {

  private readonly byte[]? _gsub;
  private readonly ConcurrentDictionary<string, OpenTypeVerticalForms?> _vertical = new(StringComparer.Ordinal);

  private PdfSystemFace(PdfSystemFonts.Face face, IPdfFontProgram program, SfntCmap cmap, byte[]? gsub) {
    this.Face = face;
    this.Program = program;
    this.Cmap = cmap;
    this._gsub = gsub;
  }

  /// <summary>Which file and face this is.</summary>
  public PdfSystemFonts.Face Face { get; }

  /// <summary>The outlines. Shared between documents and threads: lock on this object while drawing from it.</summary>
  public IPdfFontProgram Program { get; }

  /// <summary>The Unicode cmap subtable.</summary>
  public SfntCmap Cmap { get; }

  /// <summary>The character collection a CID-keyed CFF face is keyed by, or null.</summary>
  public (string Registry, string Ordering, int Supplement)? CharacterCollection => (this.Program as CffFontProgram)?.CharacterCollection;

  /// <summary>The vertical alternates for a script and language system, or null when the face has none.</summary>
  public OpenTypeVerticalForms? VerticalForms(string script, string language)
    => this._vertical.GetOrAdd(script + "/" + language, _ => OpenTypeVerticalForms.Read(this._gsub, script, language));

  /// <summary>Reads a face in full, or null when it turns out not to be drawable.</summary>
  public static PdfSystemFace? Load(PdfSystemFonts.Face face) {
    try {
      var data = File.ReadAllBytes(face.Path);
      var at = 0;
      if (data.Length >= 12 && data[0] == 't' && data[1] == 't' && data[2] == 'c' && data[3] == 'f') {
        var count = _U32(data, 8);
        if ((uint)face.Index >= count || 12 + face.Index * 4 + 4 > data.Length)
          return null;
        at = (int)_U32(data, 12 + face.Index * 4);
      }

      var tables = TrueTypeFontProgram.ReadDirectory(data, at);
      if (!tables.TryGetValue("cmap", out var cmapTable))
        return null;

      var subtable = PdfSystemFonts.UnicodeSubtable(data.AsSpan(cmapTable.Offset, cmapTable.Length).ToArray(), cmapTable.Offset);
      if (subtable < 0 || SfntCmap.Read(data, (int)subtable) is not { } cmap)
        return null;

      byte[]? gsub = tables.TryGetValue("GSUB", out var g) ? data.AsSpan(g.Offset, g.Length).ToArray() : null;
      IPdfFontProgram program = !tables.ContainsKey("glyf") && tables.TryGetValue("CFF ", out var cff)
        ? CffFontProgram.Parse(data.AsSpan(cff.Offset, cff.Length).ToArray())
        : TrueTypeFontProgram.FromTables(data, tables);
      return program.GlyphCount > 0 ? new PdfSystemFace(face, program, cmap, gsub) : null;
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or IndexOutOfRangeException
                                  or ArgumentException or OverflowException or NotSupportedException or OutOfMemoryException
                                  or System.Security.SecurityException) {
      return null;
    }
  }

  private static uint _U32(byte[] data, int at) => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
