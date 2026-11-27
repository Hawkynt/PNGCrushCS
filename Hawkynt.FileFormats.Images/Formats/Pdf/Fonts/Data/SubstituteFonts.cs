using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Pdf.Fonts.Data;

/// <summary>Outline fonts to draw a standard font with when the document does not carry it.</summary>
/// <remarks>
/// The twelve Latin faces of the standard fourteen are drawn with Liberation Sans, Serif and Mono
/// 2.1.5, which were made to the advance widths of Arial, Times New Roman and Courier New and so to
/// those of Helvetica, Times and Courier: text set for one of the standard fonts lands where the
/// document put it. They are licensed under the SIL Open Font License 1.1, which allows bundling
/// them with software under any licence as long as the fonts themselves stay under the OFL, keep
/// their copyright and licence, and are not sold on their own. The files are carried unmodified —
/// compressed with Brotli for transport and decompressed byte for byte here — so the Reserved Font
/// Name "Liberation" stays theirs; a subset or edited copy could not keep it. The licence travels
/// beside them as <c>Substitutes/OFL-Liberation.txt</c>.
/// <para/>
/// Symbol and ZapfDingbats have no freely licensed counterpart with their glyph sets and widths that
/// could be bundled under compatible terms (the URW replacements are AGPL), so there is nothing to
/// substitute for them; <see cref="Omitted"/> says so, and the caller is expected to draw nothing for
/// such text rather than something that looks like it.
/// </remarks>
internal static class SubstituteFonts {

  private const string _ResourcePrefix = "FileFormat.Pdf.Fonts.Data.Substitutes.";

  private static readonly ConcurrentDictionary<string, byte[]> _Loaded = new(StringComparer.Ordinal);

  /// <summary>The standard fonts no substitute is bundled for.</summary>
  public static IReadOnlyList<string> Omitted { get; } = ["Symbol", "ZapfDingbats"];

  /// <summary>The TrueType bytes of the substitute for a canonical standard font name, or null when there is none.</summary>
  /// <remarks>
  /// The same array is handed to every caller, so it must not be written to. Each face is
  /// decompressed once, the first time it is asked for.
  /// </remarks>
  public static byte[]? TrueTypeFor(string canonicalStandardName) {
    var file = _FileFor(canonicalStandardName);
    return file == null ? null : _Loaded.GetOrAdd(file, _Load);
  }

  private static string? _FileFor(string name) => name switch {
    "Helvetica" => "LiberationSans-Regular",
    "Helvetica-Bold" => "LiberationSans-Bold",
    "Helvetica-Oblique" => "LiberationSans-Italic",
    "Helvetica-BoldOblique" => "LiberationSans-BoldItalic",
    "Times-Roman" => "LiberationSerif-Regular",
    "Times-Bold" => "LiberationSerif-Bold",
    "Times-Italic" => "LiberationSerif-Italic",
    "Times-BoldItalic" => "LiberationSerif-BoldItalic",
    "Courier" => "LiberationMono-Regular",
    "Courier-Bold" => "LiberationMono-Bold",
    "Courier-Oblique" => "LiberationMono-Italic",
    "Courier-BoldOblique" => "LiberationMono-BoldItalic",
    _ => null,
  };

  private static byte[] _Load(string file) {
    using var stream = typeof(SubstituteFonts).Assembly.GetManifestResourceStream(_ResourcePrefix + file + ".ttf.br")
                       ?? throw new InvalidOperationException($"The bundled substitute font {file} is missing from the assembly.");
    using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
    using var output = new MemoryStream();
    brotli.CopyTo(output);
    return output.ToArray();
  }
}
