using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>
/// Translates between PNG's ancillary metadata chunks (<c>eXIf</c>, <c>tEXt</c>/<c>zTXt</c>/<c>iTXt</c>,
/// <c>iCCP</c>, <c>pHYs</c>) and the format-neutral <see cref="ImageMetadata"/> model.
/// </summary>
/// <remarks>
/// <see cref="PngReader"/> already preserves every ancillary chunk verbatim in <see cref="PngFile"/>'s
/// three <c>Chunks*</c> lists for a pure PNG-in/PNG-out round trip — this codec sits one level above
/// that, decoding the handful of chunk types the shared <see cref="ImageMetadata"/> model understands
/// so they can survive a hop through <see cref="RawImage"/> into another format. Chunk types this codec
/// doesn't recognise stay exactly as <see cref="PngReader"/> already leaves them: untouched raw bytes in
/// the <c>Chunks*</c> lists, unaffected by anything in this file.
/// <para/>
/// XMP has no dedicated PNG chunk. The de facto convention — used by Adobe's own tools and recognised
/// by exiftool — is an <c>iTXt</c> chunk with keyword <c>XML:com.adobe.xmp</c>; this codec reads and
/// writes that convention rather than inventing a new one.
/// </remarks>
internal static class PngMetadataCodec {

  private const string _XmpKeyword = "XML:com.adobe.xmp";
  private const double _MetersPerInch = 0.0254;

  /// <summary>Extracts <see cref="ImageMetadata"/> from every ancillary chunk <see cref="PngReader"/>
  /// preserved on <paramref name="file"/>. Returns <c>null</c> when nothing this codec recognises was
  /// found (an empty <see cref="ImageMetadata"/> would just make every caller check <c>IsEmpty</c>).</summary>
  public static ImageMetadata? Read(PngFile file) {
    ExifData? exif = null;
    byte[]? xmp = null;
    byte[]? icc = null;
    string? iccName = null;
    double? dpiX = null, dpiY = null;
    var texts = new List<TextMetadataEntry>();

    void Visit(IReadOnlyList<PngChunk>? chunks) {
      if (chunks == null) return;
      foreach (var chunk in chunks) {
        switch (chunk.Type) {
          case "eXIf":
            exif = ExifCodec.TryParse(chunk.Data);
            break;

          case "tEXt": {
            var (kw, text) = _SplitLatin1TextChunk(chunk.Data);
            if (kw != null) texts.Add(new TextMetadataEntry(kw, text!));
            break;
          }

          case "zTXt": {
            var (kw, text) = _SplitCompressedLatin1TextChunk(chunk.Data);
            if (kw != null) texts.Add(new TextMetadataEntry(kw, text!, PreferCompression: true));
            break;
          }

          case "iTXt": {
            var entry = _SplitInternationalTextChunk(chunk.Data);
            if (entry == null) break;
            if (entry.Value.Keyword == _XmpKeyword && entry.Value.LanguageTag is null or "" && entry.Value.TranslatedKeyword is null or "")
              xmp = Encoding.UTF8.GetBytes(entry.Value.Text);
            else
              texts.Add(entry.Value);
            break;
          }

          case "iCCP": {
            var (name, profile) = _SplitIccp(chunk.Data);
            if (name != null) { iccName = name; icc = profile; }
            break;
          }

          case "pHYs": {
            if (chunk.Data.Length < 9) break;
            var perMeterX = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(0, 4));
            var perMeterY = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(4, 4));
            var unit = chunk.Data[8];
            if (unit != 1) break; // 0 = unitless aspect ratio only — we don't fabricate a DPI for that.
            dpiX = perMeterX * _MetersPerInch;
            dpiY = perMeterY * _MetersPerInch;
            break;
          }
        }
      }
    }

    Visit(file.ChunksBeforePlte);
    Visit(file.ChunksBetweenPlteAndIdat);
    Visit(file.ChunksAfterIdat);

    if (exif == null && xmp == null && icc == null && dpiX == null && texts.Count == 0)
      return null;

    return new ImageMetadata {
      Exif = exif,
      XmpPacket = xmp,
      IccProfile = icc,
      IccProfileName = iccName,
      DpiX = dpiX,
      DpiY = dpiY,
      TextEntries = texts,
    };
  }

  /// <summary>Builds the ancillary chunks representing <paramref name="metadata"/>, bucketed into the
  /// same before-PLTE / between-PLTE-and-IDAT / after-IDAT zones <see cref="PngWriter"/> already knows
  /// how to emit. Colour/geometry hints (<c>iCCP</c>, <c>pHYs</c>) land before PLTE, which the spec
  /// always permits since "before PLTE" implies "before IDAT"; free-form annotations (<c>eXIf</c>,
  /// text) land after IDAT, where the spec places no ordering constraint on them.</summary>
  public static void Apply(ImageMetadata metadata, List<PngChunk> beforePlte, List<PngChunk> afterIdat) {
    ArgumentNullException.ThrowIfNull(metadata);

    if (metadata.IccProfile != null) {
      var name = string.IsNullOrEmpty(metadata.IccProfileName) ? "ICC Profile" : metadata.IccProfileName;
      beforePlte.Add(new PngChunk("iCCP", _BuildIccp(name, metadata.IccProfile)));
    }

    if (metadata.DpiX is { } dx && metadata.DpiY is { } dy)
      beforePlte.Add(new PngChunk("pHYs", _BuildPhys(dx, dy)));

    if (metadata.Exif != null)
      afterIdat.Add(new PngChunk("eXIf", ExifCodec.Write(metadata.Exif)));

    if (metadata.XmpPacket != null) {
      var xmpText = Encoding.UTF8.GetString(metadata.XmpPacket);
      afterIdat.Add(new PngChunk("iTXt", _BuildItxt(_XmpKeyword, "", "", xmpText, compress: false)));
    }

    foreach (var entry in metadata.TextEntries) {
      var needsUnicode = entry.LanguageTag is { Length: > 0 } || entry.TranslatedKeyword is { Length: > 0 } || !_IsLatin1(entry.Text);
      if (needsUnicode) {
        afterIdat.Add(new PngChunk("iTXt", _BuildItxt(entry.Keyword, entry.LanguageTag ?? "", entry.TranslatedKeyword ?? "", entry.Text, entry.PreferCompression)));
      } else if (entry.PreferCompression) {
        afterIdat.Add(new PngChunk("zTXt", _BuildZtxt(entry.Keyword, entry.Text)));
      } else {
        afterIdat.Add(new PngChunk("tEXt", _BuildTtxt(entry.Keyword, entry.Text)));
      }
    }
  }

  // ---- chunk-body parsing ----

  private static (string? Keyword, string? Text) _SplitLatin1TextChunk(byte[] data) {
    var nul = Array.IndexOf(data, (byte)0);
    if (nul < 0) return (null, null);
    var keyword = Encoding.Latin1.GetString(data, 0, nul);
    var text = Encoding.Latin1.GetString(data, nul + 1, data.Length - nul - 1);
    return (keyword, text);
  }

  private static (string? Keyword, string? Text) _SplitCompressedLatin1TextChunk(byte[] data) {
    var nul = Array.IndexOf(data, (byte)0);
    if (nul < 0 || nul + 2 > data.Length) return (null, null);
    var keyword = Encoding.Latin1.GetString(data, 0, nul);
    // data[nul+1] is the compression method (always 0 == zlib/deflate per spec).
    var compressed = data.AsSpan(nul + 2).ToArray();
    var inflated = _Inflate(compressed);
    return (keyword, Encoding.Latin1.GetString(inflated));
  }

  private static TextMetadataEntry? _SplitInternationalTextChunk(byte[] data) {
    var pos = Array.IndexOf(data, (byte)0);
    // Need the keyword-terminating NUL plus both the compression-flag and compression-method bytes
    // that immediately follow it — three bytes from pos, not two, or the method read below runs past
    // the end of a truncated chunk.
    if (pos < 0 || pos + 3 > data.Length) return null;
    var keyword = Encoding.Latin1.GetString(data, 0, pos);
    pos += 1;
    var compressionFlag = data[pos++];
    var compressionMethod = data[pos++];
    _ = compressionMethod;

    var langEnd = Array.IndexOf(data, (byte)0, pos);
    if (langEnd < 0) return null;
    var language = Encoding.ASCII.GetString(data, pos, langEnd - pos);
    pos = langEnd + 1;

    var translatedEnd = Array.IndexOf(data, (byte)0, pos);
    if (translatedEnd < 0) return null;
    var translated = Encoding.UTF8.GetString(data, pos, translatedEnd - pos);
    pos = translatedEnd + 1;

    var body = data.AsSpan(pos).ToArray();
    var text = compressionFlag != 0 ? Encoding.UTF8.GetString(_Inflate(body)) : Encoding.UTF8.GetString(body);

    return new TextMetadataEntry(keyword, text, language.Length > 0 ? language : null, translated.Length > 0 ? translated : null, compressionFlag != 0);
  }

  private static (string? Name, byte[]? Profile) _SplitIccp(byte[] data) {
    var nul = Array.IndexOf(data, (byte)0);
    if (nul < 0 || nul + 2 > data.Length) return (null, null);
    var name = Encoding.Latin1.GetString(data, 0, nul);
    // data[nul+1] is the compression method (always 0 per spec).
    var compressed = data.AsSpan(nul + 2).ToArray();
    return (name, _Inflate(compressed));
  }

  // ---- chunk-body building ----

  private static byte[] _BuildTtxt(string keyword, string text) {
    using var ms = new MemoryStream();
    ms.Write(Encoding.Latin1.GetBytes(keyword));
    ms.WriteByte(0);
    ms.Write(Encoding.Latin1.GetBytes(text));
    return ms.ToArray();
  }

  private static byte[] _BuildZtxt(string keyword, string text) {
    using var ms = new MemoryStream();
    ms.Write(Encoding.Latin1.GetBytes(keyword));
    ms.WriteByte(0);
    ms.WriteByte(0); // compression method = zlib/deflate
    ms.Write(_Deflate(Encoding.Latin1.GetBytes(text)));
    return ms.ToArray();
  }

  private static byte[] _BuildItxt(string keyword, string language, string translatedKeyword, string text, bool compress) {
    using var ms = new MemoryStream();
    ms.Write(Encoding.Latin1.GetBytes(keyword));
    ms.WriteByte(0);
    ms.WriteByte((byte)(compress ? 1 : 0));
    ms.WriteByte(0); // compression method = zlib/deflate
    ms.Write(Encoding.ASCII.GetBytes(language));
    ms.WriteByte(0);
    ms.Write(Encoding.UTF8.GetBytes(translatedKeyword));
    ms.WriteByte(0);
    var textBytes = Encoding.UTF8.GetBytes(text);
    ms.Write(compress ? _Deflate(textBytes) : textBytes);
    return ms.ToArray();
  }

  private static byte[] _BuildIccp(string name, byte[] profile) {
    using var ms = new MemoryStream();
    ms.Write(Encoding.Latin1.GetBytes(name));
    ms.WriteByte(0);
    ms.WriteByte(0); // compression method = zlib/deflate
    ms.Write(_Deflate(profile));
    return ms.ToArray();
  }

  private static byte[] _BuildPhys(double dpiX, double dpiY) {
    var result = new byte[9];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), (uint)Math.Round(dpiX / _MetersPerInch));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), (uint)Math.Round(dpiY / _MetersPerInch));
    result[8] = 1; // unit = meter
    return result;
  }

  // ---- helpers ----

  private static bool _IsLatin1(string text) {
    foreach (var c in text)
      if (c > 0xFF)
        return false;
    return true;
  }

  private static byte[] _Deflate(byte[] data) {
    using var ms = new MemoryStream();
    using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true))
      zlib.Write(data);
    return ms.ToArray();
  }

  private static byte[] _Inflate(byte[] data) {
    using var input = new MemoryStream(data);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }
}
