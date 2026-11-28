using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Pdf.Fonts.Data;

/// <summary>Which of the fourteen standard fonts a font that does not carry its own outlines stands for.</summary>
/// <remarks>
/// A <c>/BaseFont</c> names the font the producer had, which is the standard name only some of the
/// time: <c>Arial,Bold</c>, <c>ArialMT</c>, <c>TimesNewRomanPS-BoldItalicMT</c> and
/// <c>ABCDEF+CourierNewPSMT</c> all occur for fonts the standard fourteen cover. The two name tables
/// are pdf.js's <c>getStdFontMap</c> and <c>getNonStdFontMap</c> (<c>src/core/standard_fonts.js</c>,
/// Apache-2.0), the second kept only where it lands on one of the fourteen; the list of serif family
/// names is its <c>getSerifFonts</c>. The normalisation is pdf.js's <c>normalizeFontName</c>: commas
/// and underscores become hyphens and white space goes.
/// <para/>
/// Where the whole name is not in the tables, its family — the part before the first hyphen — may
/// still be (<c>Arial-BoldOblique</c>, <c>TimesNewRoman,Semibold</c>), and then the style is read off
/// the rest of the name, the caller's bold and italic, and the descriptor's ForceBold and Italic
/// flags. That step is this code's own and is not in pdf.js.
/// </remarks>
internal static class StandardFontNames {

  /// <summary>FontDescriptor /Flags bit 1: every glyph has the same width.</summary>
  private const int _FixedPitch = 1 << 0;

  /// <summary>FontDescriptor /Flags bit 2: glyphs have serifs.</summary>
  private const int _Serif = 1 << 1;

  /// <summary>FontDescriptor /Flags bit 7: glyphs are slanted.</summary>
  private const int _Italic = 1 << 6;

  /// <summary>FontDescriptor /Flags bit 19: draw bold even at small sizes.</summary>
  private const int _ForceBold = 1 << 18;

  private static readonly Lazy<Dictionary<string, string>> _Families = new(_BuildFamilies);

  private static readonly Lazy<HashSet<string>> _SerifFamilies = new(_BuildSerifFamilies);

  /// <summary>The canonical standard-14 name the font stands for, or null when it stands for none of them.</summary>
  /// <param name="baseFont">The font's <c>/BaseFont</c>, subset prefix and all.</param>
  /// <param name="flags">The FontDescriptor's <c>/Flags</c>, or 0 where there is none.</param>
  /// <param name="bold">Whether something else (a FontWeight of 600 or more, say) says the font is bold.</param>
  /// <param name="italic">Whether something else (a non-zero ItalicAngle, say) says the font is italic.</param>
  public static string? Canonical(string baseFont, int flags, bool bold, bool italic) {
    ArgumentNullException.ThrowIfNull(baseFont);

    var name = _Normalise(_WithoutSubsetPrefix(baseFont));
    if (name.Length == 0)
      return null;

    bold |= (flags & _ForceBold) != 0;
    italic |= (flags & _Italic) != 0;

    if (_StdFontMap.TryGetValue(name, out var mapped) || _NonStdFontMap.TryGetValue(name, out mapped))
      return _WithStyle(_FamilyOf(mapped), _IsBold(mapped) || bold, _IsItalic(mapped) || italic);

    var hyphen = name.IndexOf('-');
    var family = hyphen < 0 ? name : name[..hyphen];
    if (!_Families.Value.TryGetValue(family, out var standardFamily))
      return null;

    var style = hyphen < 0 ? string.Empty : name[(hyphen + 1)..];
    return _WithStyle(standardFamily, bold || _NamesBold(style), italic || _NamesItalic(style));
  }

  /// <summary>The standard font to use for a font the tables do not know, chosen by its flags.</summary>
  /// <remarks>Fixed-pitch is Courier, serif is Times and anything else is Helvetica.</remarks>
  public static string Fallback(int flags, bool bold, bool italic) {
    bold |= (flags & _ForceBold) != 0;
    italic |= (flags & _Italic) != 0;
    var family = (flags & _FixedPitch) != 0 ? "Courier" : (flags & _Serif) != 0 ? "Times" : "Helvetica";
    return _WithStyle(family, bold, italic);
  }

  /// <summary>As <see cref="Fallback(int, bool, bool)"/>, and also taking a family the name says has serifs.</summary>
  /// <remarks>
  /// The Serif flag is often missing where it should be set, which is why pdf.js falls back to the
  /// name; this does the same against the same list. Its multi-word entries ("Book Antiqua") are
  /// compared with their spaces removed, since a normalised font name has none.
  /// </remarks>
  public static string Fallback(string baseFont, int flags, bool bold, bool italic) {
    ArgumentNullException.ThrowIfNull(baseFont);
    if ((flags & (_FixedPitch | _Serif)) == 0 && _IsSerifName(baseFont))
      flags |= _Serif;

    return Fallback(flags, bold, italic);
  }

  private static bool _IsSerifName(string baseFont) {
    foreach (var part in baseFont.Split('+')) {
      var name = _Normalise(part);
      if (_StdFontMap.TryGetValue(name, out var mapped) || _NonStdFontMap.TryGetValue(name, out mapped))
        name = mapped;

      var hyphen = name.IndexOf('-');
      if (_SerifFamilies.Value.Contains(hyphen < 0 ? name : name[..hyphen]))
        return true;
    }

    return false;
  }

  private static string _WithoutSubsetPrefix(string name) {
    if (name.Length < 8 || name[6] != '+')
      return name;

    for (var i = 0; i < 6; ++i)
      if (name[i] is not (>= 'A' and <= 'Z'))
        return name;

    return name[7..];
  }

  private static string _Normalise(string name) {
    var builder = new StringBuilder(name.Length);
    foreach (var c in name)
      if (c is ',' or '_')
        builder.Append('-');
      else if (!char.IsWhiteSpace(c))
        builder.Append(c);

    return builder.ToString();
  }

  private static string _FamilyOf(string canonical) => canonical switch {
    "Symbol" or "ZapfDingbats" => canonical,
    _ when canonical.StartsWith("Courier", StringComparison.Ordinal) => "Courier",
    _ when canonical.StartsWith("Times", StringComparison.Ordinal) => "Times",
    _ => "Helvetica",
  };

  private static bool _IsBold(string canonical) => canonical.Contains("Bold", StringComparison.Ordinal);

  private static bool _IsItalic(string canonical) => canonical.Contains("Italic", StringComparison.Ordinal) || canonical.Contains("Oblique", StringComparison.Ordinal);

  private static bool _NamesBold(string style)
    => style.Contains("Bold", StringComparison.OrdinalIgnoreCase)
       || style.Contains("Black", StringComparison.OrdinalIgnoreCase)
       || style.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
       || style.Contains("Demi", StringComparison.OrdinalIgnoreCase)
       || style.Contains("Semibold", StringComparison.OrdinalIgnoreCase);

  private static bool _NamesItalic(string style)
    => style.Contains("Italic", StringComparison.OrdinalIgnoreCase)
       || style.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
       || style.Contains("Inclined", StringComparison.OrdinalIgnoreCase);

  private static string _WithStyle(string family, bool bold, bool italic) => family switch {
    "Symbol" or "ZapfDingbats" => family,
    "Times" => (bold, italic) switch {
      (true, true) => "Times-BoldItalic",
      (true, false) => "Times-Bold",
      (false, true) => "Times-Italic",
      _ => "Times-Roman",
    },
    _ => (bold, italic) switch {
      (true, true) => family + "-BoldOblique",
      (true, false) => family + "-Bold",
      (false, true) => family + "-Oblique",
      _ => family,
    },
  };

  private static Dictionary<string, string> _BuildFamilies() {
    var families = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var map in new[] { _StdFontMap, _NonStdFontMap })
      foreach (var (key, value) in map) {
        var hyphen = key.IndexOf('-');
        families.TryAdd(hyphen < 0 ? key : key[..hyphen], _FamilyOf(value));
      }

    return families;
  }

  private static HashSet<string> _BuildSerifFamilies() {
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var name in _SerifFonts)
      set.Add(name.Replace(" ", string.Empty, StringComparison.Ordinal));

    return set;
  }

  /// <summary>pdf.js <c>getStdFontMap</c>: names that are, or are aliases of, a standard font.</summary>
  private static readonly Dictionary<string, string> _StdFontMap = new(StringComparer.Ordinal) {
    ["Times-Roman"] = "Times-Roman",
    ["Helvetica"] = "Helvetica",
    ["Courier"] = "Courier",
    ["Symbol"] = "Symbol",
    ["Times-Bold"] = "Times-Bold",
    ["Helvetica-Bold"] = "Helvetica-Bold",
    ["Courier-Bold"] = "Courier-Bold",
    ["ZapfDingbats"] = "ZapfDingbats",
    ["Times-Italic"] = "Times-Italic",
    ["Helvetica-Oblique"] = "Helvetica-Oblique",
    ["Courier-Oblique"] = "Courier-Oblique",
    ["Times-BoldItalic"] = "Times-BoldItalic",
    ["Helvetica-BoldOblique"] = "Helvetica-BoldOblique",
    ["Courier-BoldOblique"] = "Courier-BoldOblique",
    ["ArialNarrow"] = "Helvetica",
    ["ArialNarrow-Bold"] = "Helvetica-Bold",
    ["ArialNarrow-BoldItalic"] = "Helvetica-BoldOblique",
    ["ArialNarrow-Italic"] = "Helvetica-Oblique",
    ["ArialBlack"] = "Helvetica",
    ["ArialBlack-Bold"] = "Helvetica-Bold",
    ["ArialBlack-BoldItalic"] = "Helvetica-BoldOblique",
    ["ArialBlack-Italic"] = "Helvetica-Oblique",
    ["Arial-Black"] = "Helvetica",
    ["Arial-Black-Bold"] = "Helvetica-Bold",
    ["Arial-Black-BoldItalic"] = "Helvetica-BoldOblique",
    ["Arial-Black-Italic"] = "Helvetica-Oblique",
    ["Arial"] = "Helvetica",
    ["Arial-Bold"] = "Helvetica-Bold",
    ["Arial-BoldItalic"] = "Helvetica-BoldOblique",
    ["Arial-Italic"] = "Helvetica-Oblique",
    ["ArialMT"] = "Helvetica",
    ["Arial-BoldItalicMT"] = "Helvetica-BoldOblique",
    ["Arial-BoldMT"] = "Helvetica-Bold",
    ["Arial-ItalicMT"] = "Helvetica-Oblique",
    ["Arial-BoldItalicMT-BoldItalic"] = "Helvetica-BoldOblique",
    ["Arial-BoldMT-Bold"] = "Helvetica-Bold",
    ["Arial-ItalicMT-Italic"] = "Helvetica-Oblique",
    ["ArialUnicodeMS"] = "Helvetica",
    ["ArialUnicodeMS-Bold"] = "Helvetica-Bold",
    ["ArialUnicodeMS-BoldItalic"] = "Helvetica-BoldOblique",
    ["ArialUnicodeMS-Italic"] = "Helvetica-Oblique",
    ["Courier-BoldItalic"] = "Courier-BoldOblique",
    ["Courier-Italic"] = "Courier-Oblique",
    ["CourierNew"] = "Courier",
    ["CourierNew-Bold"] = "Courier-Bold",
    ["CourierNew-BoldItalic"] = "Courier-BoldOblique",
    ["CourierNew-Italic"] = "Courier-Oblique",
    ["CourierNewPS-BoldItalicMT"] = "Courier-BoldOblique",
    ["CourierNewPS-BoldMT"] = "Courier-Bold",
    ["CourierNewPS-ItalicMT"] = "Courier-Oblique",
    ["CourierNewPSMT"] = "Courier",
    ["Helvetica-BoldItalic"] = "Helvetica-BoldOblique",
    ["Helvetica-Italic"] = "Helvetica-Oblique",
    ["HelveticaLTStd-Bold"] = "Helvetica-Bold",
    ["Symbol-Bold"] = "Symbol",
    ["Symbol-BoldItalic"] = "Symbol",
    ["Symbol-Italic"] = "Symbol",
    ["TimesNewRoman"] = "Times-Roman",
    ["TimesNewRoman-Bold"] = "Times-Bold",
    ["TimesNewRoman-BoldItalic"] = "Times-BoldItalic",
    ["TimesNewRoman-Italic"] = "Times-Italic",
    ["TimesNewRomanPS"] = "Times-Roman",
    ["TimesNewRomanPS-Bold"] = "Times-Bold",
    ["TimesNewRomanPS-BoldItalic"] = "Times-BoldItalic",
    ["TimesNewRomanPS-BoldItalicMT"] = "Times-BoldItalic",
    ["TimesNewRomanPS-BoldMT"] = "Times-Bold",
    ["TimesNewRomanPS-Italic"] = "Times-Italic",
    ["TimesNewRomanPS-ItalicMT"] = "Times-Italic",
    ["TimesNewRomanPSMT"] = "Times-Roman",
    ["TimesNewRomanPSMT-Bold"] = "Times-Bold",
    ["TimesNewRomanPSMT-BoldItalic"] = "Times-BoldItalic",
    ["TimesNewRomanPSMT-Italic"] = "Times-Italic",
  };

  /// <summary>pdf.js <c>getNonStdFontMap</c>, the entries that land on one of the fourteen.</summary>
  private static readonly Dictionary<string, string> _NonStdFontMap = new(StringComparer.Ordinal) {
    ["Calibri"] = "Helvetica",
    ["Calibri-Bold"] = "Helvetica-Bold",
    ["Calibri-BoldItalic"] = "Helvetica-BoldOblique",
    ["Calibri-Italic"] = "Helvetica-Oblique",
    ["CenturyGothic"] = "Helvetica",
    ["CenturyGothic-Bold"] = "Helvetica-Bold",
    ["CenturyGothic-BoldItalic"] = "Helvetica-BoldOblique",
    ["CenturyGothic-Italic"] = "Helvetica-Oblique",
    ["GillSansMT"] = "Helvetica",
    ["GillSansMT-Bold"] = "Helvetica-Bold",
    ["GillSansMT-BoldItalic"] = "Helvetica-BoldOblique",
    ["GillSansMT-Italic"] = "Helvetica-Oblique",
    ["Impact"] = "Helvetica",
    ["ItcSymbol-Bold"] = "Helvetica-Bold",
    ["ItcSymbol-BoldItalic"] = "Helvetica-BoldOblique",
    ["ItcSymbol-Book"] = "Helvetica",
    ["ItcSymbol-BookItalic"] = "Helvetica-Oblique",
    ["ItcSymbol-Medium"] = "Helvetica",
    ["ItcSymbol-MediumItalic"] = "Helvetica-Oblique",
    ["LucidaConsole"] = "Courier",
    ["LucidaConsole-Bold"] = "Courier-Bold",
    ["LucidaConsole-BoldItalic"] = "Courier-BoldOblique",
    ["LucidaConsole-Italic"] = "Courier-Oblique",
    ["LucidaSans-Demi"] = "Helvetica-Bold",
    ["NuptialScript"] = "Times-Italic",
    ["SegoeUISymbol"] = "Helvetica",
    ["TrebuchetMS"] = "Helvetica",
    ["TrebuchetMS-Bold"] = "Helvetica-Bold",
    ["TrebuchetMS-BoldItalic"] = "Helvetica-BoldOblique",
    ["TrebuchetMS-Italic"] = "Helvetica-Oblique",
  };

  /// <summary>pdf.js <c>getSerifFonts</c>: families with serifs, by name.</summary>
  private static readonly string[] _SerifFonts = [
    "Adobe Jenson", "Adobe Text", "Albertus", "Aldus", "Alexandria", "Algerian",
    "American Typewriter", "Antiqua", "Apex", "Arno", "Aster", "Aurora",
    "Baskerville", "Bell", "Bembo", "Bembo Schoolbook", "Benguiat", "Berkeley Old Style",
    "Bernhard Modern", "Berthold City", "Bodoni", "Bauer Bodoni", "Book Antiqua", "Bookman",
    "Bordeaux Roman", "Californian FB", "Calisto", "Calvert", "Capitals", "Cambria",
    "Cartier", "Caslon", "Catull", "Centaur", "Century Old Style", "Century Schoolbook",
    "Chaparral", "Charis SIL", "Cheltenham", "Cholla Slab", "Clarendon", "Clearface",
    "Cochin", "Colonna", "Computer Modern", "Concrete Roman", "Constantia", "Cooper Black",
    "Corona", "Ecotype", "Egyptienne", "Elephant", "Excelsior", "Fairfield",
    "FF Scala", "Folkard", "Footlight", "FreeSerif", "Friz Quadrata", "Garamond",
    "Gentium", "Georgia", "Gloucester", "Goudy Old Style", "Goudy Schoolbook", "Goudy Pro Font",
    "Granjon", "Guardian Egyptian", "Heather", "Hercules", "High Tower Text", "Hiroshige",
    "Hoefler Text", "Humana Serif", "Imprint", "Ionic No. 5", "Janson", "Joanna",
    "Korinna", "Lexicon", "LiberationSerif", "Liberation Serif", "Linux Libertine", "Literaturnaya",
    "Lucida", "Lucida Bright", "Melior", "Memphis", "Miller", "Minion",
    "Modern", "Mona Lisa", "Mrs Eaves", "MS Serif", "Museo Slab", "New York",
    "Nimbus Roman", "NPS Rawlinson Roadway", "NuptialScript", "Palatino", "Perpetua", "Plantin",
    "Plantin Schoolbook", "Playbill", "Poor Richard", "Rawlinson Roadway", "Renault", "Requiem",
    "Rockwell", "Roman", "Rotis Serif", "Sabon", "Scala", "Seagull",
    "Sistina", "Souvenir", "STIX", "Stone Informal", "Stone Serif", "Sylfaen",
    "Times", "Trajan", "Trinité", "Trump Mediaeval", "Utopia", "Vale Type",
    "Bitstream Vera", "Vera Serif", "Versailles", "Wanted", "Weiss", "Wide Latin",
    "Windsor", "XITS",
  ];
}
