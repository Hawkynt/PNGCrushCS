using System;
using System.Collections.Generic;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Jpeg;

/// <summary>
/// Translates between the JPEG marker segments that carry metadata (EXIF- and XMP-flavoured APP1,
/// Photoshop-flavoured APP13 for IPTC, and COM) and the format-neutral <see cref="ImageMetadata"/> model.
/// </summary>
/// <remarks>
/// JPEG has no keyword concept for free text (unlike PNG's tEXt/iTXt), so every
/// <see cref="TextMetadataEntry"/> collapses into its own COM segment on write — prefixed with
/// <c>"Keyword: "</c> unless the keyword is empty or literally <c>"Comment"</c>, so nothing about a
/// carried-over PNG keyword is silently discarded even though JPEG can't represent it structurally.
/// COM text is written as Latin-1 (.NET's <see cref="Encoding.Latin1"/> replaces anything outside that
/// range with <c>?</c> rather than throwing) since JPEG's COM segment has no declared charset and
/// Latin-1/ASCII is what every reader assumes.
/// <para/>
/// A single JPEG marker segment's length field is 16-bit, capping any one segment's payload at 65533
/// bytes. A facet that doesn't fit is dropped from the output rather than silently truncated or
/// wrapped to a bogus (and corrupting) length — see <see cref="_FitsInOneSegment"/>.
/// </remarks>
internal static class JpegMetadataCodec {

  private static ReadOnlySpan<byte> _ExifPrefix => "Exif\0\0"u8;
  private static ReadOnlySpan<byte> _XmpPrefix => "http://ns.adobe.com/xap/1.0/\0"u8;
  private static ReadOnlySpan<byte> _PhotoshopPrefix => "Photoshop 3.0\0"u8;
  private const byte _App13 = JpegMarker.APP0 + 13;
  private const int _MaxSegmentPayload = 65533; // 0xFFFF length field minus the 2 length bytes themselves.

  /// <summary>Parses every APP1/APP13/COM segment out of a raw JPEG byte stream. Returns <c>null</c>
  /// when none of them carry anything this codec recognises.</summary>
  public static ImageMetadata? Read(byte[] rawJpegBytes) {
    var image = JpegMarkerParser.ParseAllMarkers(rawJpegBytes);
    return FromMarkerSegments(image.MarkerSegments);
  }

  /// <summary>Parses metadata out of an already-extracted marker-segment list (used by the lossless
  /// transcode path, which has these in hand from <see cref="JpegManagedDecoder.DecodeToCoefficients"/>
  /// already).</summary>
  public static ImageMetadata? FromMarkerSegments(IReadOnlyList<JpegMarkerSegment> segments) {
    ExifData? exif = null;
    byte[]? xmp = null;
    IptcData? iptc = null;
    var texts = new List<TextMetadataEntry>();

    foreach (var seg in segments) {
      if (seg.Marker == JpegMarker.APP1 && _StartsWith(seg.Data, _ExifPrefix)) {
        exif = ExifCodec.TryParse(seg.Data.AsSpan(_ExifPrefix.Length));
      } else if (seg.Marker == JpegMarker.APP1 && _StartsWith(seg.Data, _XmpPrefix)) {
        xmp = seg.Data[_XmpPrefix.Length..];
      } else if (seg.Marker == _App13 && _StartsWith(seg.Data, _PhotoshopPrefix)) {
        iptc = IptcCodec.TryParsePhotoshopSegment(seg.Data);
      } else if (seg.Marker == JpegMarker.COM) {
        texts.Add(new TextMetadataEntry("", Encoding.Latin1.GetString(seg.Data)));
      }
    }

    if (exif == null && xmp == null && iptc == null && texts.Count == 0)
      return null;

    return new ImageMetadata { Exif = exif, XmpPacket = xmp, Iptc = iptc, TextEntries = texts };
  }

  /// <summary>Builds the marker segments representing <paramref name="metadata"/>, in a stable
  /// EXIF/XMP/IPTC/COM* order. Segments that would exceed one marker's 16-bit length field are
  /// omitted — see type-level remarks.</summary>
  public static List<JpegMarkerSegment> ToMarkerSegments(ImageMetadata metadata) {
    ArgumentNullException.ThrowIfNull(metadata);
    var result = new List<JpegMarkerSegment>();

    if (metadata.Exif != null) {
      var tiff = ExifCodec.Write(metadata.Exif);
      if (_FitsInOneSegment(_ExifPrefix.Length + tiff.Length))
        result.Add(new JpegMarkerSegment { Marker = JpegMarker.APP1, Data = _Concat(_ExifPrefix, tiff) });
    }

    if (metadata.XmpPacket != null && _FitsInOneSegment(_XmpPrefix.Length + metadata.XmpPacket.Length))
      result.Add(new JpegMarkerSegment { Marker = JpegMarker.APP1, Data = _Concat(_XmpPrefix, metadata.XmpPacket) });

    if (metadata.Iptc != null) {
      var segment = IptcCodec.ToPhotoshopSegment(metadata.Iptc);
      if (_FitsInOneSegment(segment.Length))
        result.Add(new JpegMarkerSegment { Marker = _App13, Data = segment });
    }

    foreach (var entry in metadata.TextEntries) {
      var text = string.IsNullOrEmpty(entry.Keyword) || entry.Keyword == "Comment" ? entry.Text : $"{entry.Keyword}: {entry.Text}";
      var bytes = Encoding.Latin1.GetBytes(text);
      if (_FitsInOneSegment(bytes.Length))
        result.Add(new JpegMarkerSegment { Marker = JpegMarker.COM, Data = bytes });
    }

    return result;
  }

  private static bool _FitsInOneSegment(int payloadLength) => payloadLength <= _MaxSegmentPayload;

  private static bool _StartsWith(byte[] data, ReadOnlySpan<byte> prefix)
    => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

  private static byte[] _Concat(ReadOnlySpan<byte> prefix, byte[] rest) {
    var result = new byte[prefix.Length + rest.Length];
    prefix.CopyTo(result);
    rest.CopyTo(result.AsSpan(prefix.Length));
    return result;
  }
}
