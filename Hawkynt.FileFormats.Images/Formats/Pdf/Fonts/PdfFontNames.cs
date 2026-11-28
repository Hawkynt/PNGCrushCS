using System;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFormat.Pdf.Fonts;

/// <summary>
/// What the name of a CJK font says about it: the character collection it is made for and whether
/// its design has serifs — for matching a document's non-embedded CID font with an installed one.
/// </summary>
/// <remarks>
/// A hypothesis assembled from the names the vendors give their fonts (Microsoft's, Apple's, Adobe's,
/// the free families of the Linux distributions) and from fontconfig's own grouping of them into
/// serif and sans-serif in <c>40-nonlatin.conf</c>; it decides only which installed face is tried
/// first, never whether text is drawn.
/// </remarks>
internal static class PdfFontNames {

  // First match wins; the specific names come before the words they contain.
  private static readonly (string Word, bool Serif)[] _Styles = [
    ("pingfang", false), ("heiseikakugo", false), ("sans", false), ("serif", true),
    ("mincho", true), ("kozmin", true), ("heiseimin", true), ("ryumin", true), ("simsun", true),
    ("gothic", false), ("goth", false), ("kakugo", false), ("kozgo", false), ("simhei", false),
    ("ming", true), ("song", true), ("sung", true), ("batang", true), ("myungjo", true), ("myeongjo", true),
    ("gungsuh", true), ("kai", true), ("fang", true),
    ("hei", false), ("gulim", false), ("dotum", false), ("meiryo", false), ("malgun", false), ("maru", false),
    ("明朝", true), ("宋", true), ("ゴシック", false), ("黑", false), ("黒", false),
  ];

  private static readonly (string Prefix, string Ordering)[] _Families = [
    ("msgothic", "Japan1"), ("mspgothic", "Japan1"), ("msuigothic", "Japan1"), ("msmincho", "Japan1"), ("mspmincho", "Japan1"),
    ("meiryo", "Japan1"), ("yugoth", "Japan1"), ("yumincho", "Japan1"), ("ipa", "Japan1"), ("takao", "Japan1"),
    ("vlgothic", "Japan1"), ("vlpgothic", "Japan1"), ("hiragino", "Japan1"), ("osaka", "Japan1"), ("kochi", "Japan1"),
    ("sazanami", "Japan1"), ("umegothic", "Japan1"), ("umemincho", "Japan1"), ("kozuka", "Japan1"), ("kozmin", "Japan1"),
    ("kozgo", "Japan1"), ("ryumin", "Japan1"), ("heisei", "Japan1"), ("bizud", "Japan1"), ("toppan", "Japan1"),
    ("motoya", "Japan1"), ("tsukushi", "Japan1"), ("klee", "Japan1"), ("mplus", "Japan1"), ("migmix", "Japan1"),
    ("migu", "Japan1"), ("harano", "Japan1"),
    ("simsun", "GB1"), ("nsimsun", "GB1"), ("simhei", "GB1"), ("simkai", "GB1"), ("simfang", "GB1"), ("fangsong", "GB1"),
    ("kaiti", "GB1"), ("microsoftyahei", "GB1"), ("dengxian", "GB1"), ("stsong", "GB1"), ("stheiti", "GB1"),
    ("stkaiti", "GB1"), ("stfangsong", "GB1"), ("stxihei", "GB1"), ("stzhongsong", "GB1"), ("heitisc", "GB1"),
    ("songtisc", "GB1"), ("kaitisc", "GB1"), ("wenquanyi", "GB1"), ("wqy", "GB1"), ("arplsungtigb", "GB1"),
    ("arplkaitimgb", "GB1"), ("droidsansfallback", "GB1"), ("adobesong", "GB1"), ("adobeheiti", "GB1"),
    ("adobefangsong", "GB1"), ("adobekaiti", "GB1"),
    ("mingliu", "CNS1"), ("pmingliu", "CNS1"), ("microsoftjhenghei", "CNS1"), ("dfkai", "CNS1"), ("heititc", "CNS1"),
    ("songtitc", "CNS1"), ("arplmingti2lbig5", "CNS1"), ("arplkaitimbig5", "CNS1"), ("biaukai", "CNS1"), ("lihei", "CNS1"),
    ("lisong", "CNS1"), ("adobeming", "CNS1"), ("msung", "CNS1"), ("mhei", "CNS1"),
    ("batang", "Korea1"), ("gulim", "Korea1"), ("dotum", "Korea1"), ("gungsuh", "Korea1"), ("malgun", "Korea1"),
    ("applesdgothic", "Korea1"), ("applemyungjo", "Korea1"), ("applegothic", "Korea1"), ("nanum", "Korea1"),
    ("undotum", "Korea1"), ("unbatang", "Korea1"), ("baekmuk", "Korea1"), ("adobemyungjo", "Korea1"),
    ("adobegothic", "Korea1"), ("hysmyeongjo", "Korea1"), ("hygothic", "Korea1"),
  ];

  /// <summary>A name lower-cased with its spaces, hyphens, underscores and commas taken out, and any subset tag.</summary>
  public static string Normalize(string? name) {
    if (string.IsNullOrEmpty(name))
      return "";
    if (name.Length > 7 && name[6] == '+' && _AllUpper(name.AsSpan(0, 6)))
      name = name[7..];

    var builder = new StringBuilder(name.Length);
    foreach (var c in name)
      if (c is not (' ' or '-' or '_' or ',' or '\0'))
        builder.Append(char.ToLowerInvariant(c));
    return builder.ToString();
  }

  /// <summary>The Adobe ordering a family name says it is made for, or null.</summary>
  public static string? Region(string? name) {
    if (string.IsNullOrEmpty(name))
      return null;

    // Pan-CJK families carry the region in a suffix: "Noto Sans CJK JP", "Source Han Serif K", "NotoSansCJKjp-Regular".
    var tagged = Regex.Match(name, @"(?:CJK|Han\s?(?:Sans|Serif|Code\s?JP)|^Noto\s?(?:Sans|Serif)(?:\s?Mono)?)\s?(JP|J|KR|K|SC|TC|HK|HC|CN|TW)(?![a-z])", RegexOptions.IgnoreCase);
    var token = tagged.Success ? tagged.Groups[1].Value : null;
    if (token == null)
      foreach (var part in name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        if (part is "JP" or "KR" or "SC" or "TC" or "HK" or "CN" or "TW" or "Japanese" or "Korean")
          token = part;

    switch (token?.ToUpperInvariant()) {
      case "JP" or "J" or "JAPANESE":
        return "Japan1";
      case "KR" or "K" or "KOREAN":
        return "Korea1";
      case "SC" or "CN":
        return "GB1";
      case "TC" or "HK" or "HC" or "TW":
        return "CNS1";
    }

    var normalized = Normalize(name);
    foreach (var (prefix, ordering) in _Families)
      if (normalized.StartsWith(prefix, StringComparison.Ordinal))
        return ordering;
    return null;
  }

  /// <summary>Whether a name says its design has serifs (true), has none (false), or does not say (null).</summary>
  public static bool? IsSerif(string? name) {
    var normalized = Normalize(name);
    if (normalized.Length == 0)
      return null;

    foreach (var (word, serif) in _Styles)
      if (normalized.Contains(word, StringComparison.Ordinal))
        return serif;
    return null;
  }

  /// <summary>Whether a name says the face is bold.</summary>
  public static bool IsBold(string? name) {
    var normalized = Normalize(name);
    return normalized.Contains("bold", StringComparison.Ordinal) || normalized.Contains("heavy", StringComparison.Ordinal)
           || normalized.Contains("black", StringComparison.Ordinal) || Regex.IsMatch(normalized, "w[7-9]$");
  }

  /// <summary>The OS/2 code page bits (ulCodePageRange1) that stand for a collection's legacy encoding.</summary>
  public static uint CodePageBits(string ordering) => ordering switch {
    "Japan1" => 1u << 17,
    "GB1" => 1u << 18,
    "Korea1" => (1u << 19) | (1u << 21),
    "CNS1" => 1u << 20,
    _ => 0,
  };

  private static bool _AllUpper(ReadOnlySpan<char> span) {
    foreach (var c in span)
      if (c is < 'A' or > 'Z')
        return false;
    return true;
  }
}
