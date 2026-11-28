// Table values copied verbatim from pdf.js (Apache-2.0); see THIRD-PARTY-NOTICE.pdfjs.txt.
namespace FileFormat.Pdf.Fonts;

/// <summary>Adobe StandardEncoding, which a Type 1 font uses unless it states its own and which seac names its parts in.</summary>
internal static class Type1StandardEncoding {

  private static readonly string[] _Names = [
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "space", "exclam", "quotedbl", "numbersign",
    "dollar", "percent", "ampersand", "quoteright", "parenleft", "parenright",
    "asterisk", "plus", "comma", "hyphen", "period", "slash",
    "zero", "one", "two", "three", "four", "five",
    "six", "seven", "eight", "nine", "colon", "semicolon",
    "less", "equal", "greater", "question", "at", "A",
    "B", "C", "D", "E", "F", "G",
    "H", "I", "J", "K", "L", "M",
    "N", "O", "P", "Q", "R", "S",
    "T", "U", "V", "W", "X", "Y",
    "Z", "bracketleft", "backslash", "bracketright", "asciicircum", "underscore",
    "quoteleft", "a", "b", "c", "d", "e",
    "f", "g", "h", "i", "j", "k",
    "l", "m", "n", "o", "p", "q",
    "r", "s", "t", "u", "v", "w",
    "x", "y", "z", "braceleft", "bar", "braceright",
    "asciitilde", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "exclamdown",
    "cent", "sterling", "fraction", "yen", "florin", "section",
    "currency", "quotesingle", "quotedblleft", "guillemotleft", "guilsinglleft", "guilsinglright",
    "fi", "fl", "", "endash", "dagger", "daggerdbl",
    "periodcentered", "", "paragraph", "bullet", "quotesinglbase", "quotedblbase",
    "quotedblright", "guillemotright", "ellipsis", "perthousand", "", "questiondown",
    "", "grave", "acute", "circumflex", "tilde", "macron",
    "breve", "dotaccent", "dieresis", "", "ring", "cedilla",
    "", "hungarumlaut", "ogonek", "caron", "emdash", "",
    "", "", "", "", "", "",
    "", "", "", "", "", "",
    "", "", "", "AE", "", "ordfeminine",
    "", "", "", "", "Lslash", "Oslash",
    "OE", "ordmasculine", "", "", "", "",
    "", "ae", "", "", "", "dotlessi",
    "", "", "lslash", "oslash", "oe", "germandbls",
    "", "", "", "",
  ];

  /// <summary>The glyph name at a code, or null where the encoding leaves the code unmapped.</summary>
  public static string? NameOf(int code) => (uint)code < (uint)_Names.Length && _Names[code].Length > 0 ? _Names[code] : null;
}
