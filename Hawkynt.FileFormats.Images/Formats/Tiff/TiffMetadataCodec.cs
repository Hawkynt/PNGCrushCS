using System;
using System.Collections.Generic;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Tiff;

/// <summary>Carries metadata in and out of a TIFF, which keeps all of it as ordinary IFD tags.</summary>
/// <remarks>
/// TIFF needs no separate metadata container because it is one. EXIF is a TIFF stream — the same
/// header, the same IFD, the same tag entries — so a whole TIFF file parses as EXIF directly and
/// IFD0 comes back with every tag the file carries, the picture's own among them.
/// <para/>
/// The other facets are tags of IFD0 too rather than a format of their own: XMP at 0x02BC, the
/// Photoshop IPTC block at 0x83BB, an ICC profile at 0x8773, and the resolution pair with the unit
/// that says what it is measured in. So all of it is read from the one parse, and none of it needs a
/// second decoder.
/// <para/>
/// Only an absolute unit gives a density. A TIFF stating unit 1 is declaring that its resolution is
/// an aspect ratio and not a measurement, and a DPI is not invented from it.
/// </remarks>
public static class TiffMetadataCodec {

  /// <summary>Where TIFF keeps each facet, all of them tags of IFD0.</summary>
  internal const ushort TagImageDescription = 0x010E, TagSoftware = 0x0131, TagArtist = 0x013B,
    TagCopyright = 0x8298, TagXResolution = 0x011A, TagYResolution = 0x011B,
    TagResolutionUnit = 0x0128, TagXmp = 0x02BC, TagIptc = 0x83BB, TagIcc = 0x8773;

  /// <summary>The resolution unit values: 1 means the pair is a ratio and not a measurement.</summary>
  private const int _UnitNone = 1, _UnitInch = 2, _UnitCentimetre = 3;

  /// <summary>Centimetres to the inch, for a file that measures its resolution the other way.</summary>
  private const double _PerInch = 2.54;

  /// <summary>Reads everything a TIFF carries beside its pixels, or null if it carries none of it.</summary>
  public static ImageMetadata? Read(ReadOnlySpan<byte> data) {
    var exif = ExifCodec.TryParse(data);
    if (exif == null)
      return null;

    var ifd0 = exif.Ifd0;
    var (dpiX, dpiY) = _Density(exif, ifd0);

    var text = new List<TextMetadataEntry>();
    _AddText(text, "Description", ifd0.Find(TagImageDescription));
    _AddText(text, "Software", ifd0.Find(TagSoftware));
    _AddText(text, "Author", ifd0.Find(TagArtist));
    _AddText(text, "Copyright", ifd0.Find(TagCopyright));

    var metadata = new ImageMetadata {
      Exif = exif,
      XmpPacket = ifd0.Find(TagXmp)?.RawBytes,
      Iptc = _Iptc(ifd0),
      IccProfile = ifd0.Find(TagIcc)?.RawBytes,
      DpiX = dpiX,
      DpiY = dpiY,
      TextEntries = text,
    };

    return metadata.IsEmpty ? null : metadata;
  }

  private static IptcData? _Iptc(ExifIfd ifd0) {
    var entry = ifd0.Find(TagIptc);

    return entry == null ? null : IptcCodec.TryParsePhotoshopSegment(entry.RawBytes);
  }

  private static void _AddText(List<TextMetadataEntry> into, string keyword, ExifTagEntry? entry) {
    if (entry == null || entry.Type != ExifTagType.Ascii)
      return;

    var value = ExifData.DecodeAscii(entry);
    if (value.Length > 0)
      into.Add(new(keyword, value));
  }

  private static (double? X, double? Y) _Density(ExifData exif, ExifIfd ifd0) {
    var unitEntry = ifd0.Find(TagResolutionUnit);
    var unit = unitEntry == null ? _UnitInch : exif.DecodeShort(unitEntry);

    // A ratio is not a measurement, and a density is not invented from one.
    if (unit is not (_UnitInch or _UnitCentimetre))
      return (null, null);

    var scale = unit == _UnitCentimetre ? _PerInch : 1.0;

    return (_Rational(exif, ifd0.Find(TagXResolution), scale), _Rational(exif, ifd0.Find(TagYResolution), scale));
  }

  private static double? _Rational(ExifData exif, ExifTagEntry? entry, double scale) {
    if (entry == null || entry.Type != ExifTagType.Rational || entry.RawBytes.Length < 8)
      return null;

    var rationals = exif.DecodeRationals(entry);
    var value = rationals.Length > 0 ? rationals[0].ToDouble() : 0;

    return value > 0 ? value * scale : null;
  }

  /// <summary>The ASCII a text entry should be written back as, or null when there is none.</summary>
  internal static string? TextFor(ImageMetadata metadata, string keyword) {
    foreach (var entry in metadata.TextEntries)
      if (string.Equals(entry.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
        return entry.Text;

    return null;
  }

  /// <summary>Writes every facet the format can hold onto the directory being built.</summary>
  /// <remarks>
  /// The tags go on before the pixels because a TIFF directory is written once, when the strips are
  /// flushed, and a field set afterwards would not reach the file.
  /// <para/>
  /// EXIF is deliberately not written back as a sub-IFD. Reading takes the picture's own IFD0 as the
  /// EXIF it is, so writing it again as a nested copy would put every tag in the file twice — once
  /// where the format keeps it and once inside a pointer — and the two would then disagree the
  /// moment anything edited one of them. What EXIF carries that this format has a place for is
  /// written to that place instead.
  /// </remarks>
  internal static void Apply(ImageMetadata metadata, BitMiracle.LibTiff.Classic.Tiff tiff) {
    ArgumentNullException.ThrowIfNull(metadata);
    ArgumentNullException.ThrowIfNull(tiff);

    _SetText(tiff, BitMiracle.LibTiff.Classic.TiffTag.IMAGEDESCRIPTION, TextFor(metadata, "Description"));
    _SetText(tiff, BitMiracle.LibTiff.Classic.TiffTag.SOFTWARE, TextFor(metadata, "Software"));
    _SetText(tiff, BitMiracle.LibTiff.Classic.TiffTag.ARTIST, TextFor(metadata, "Author"));
    _SetText(tiff, BitMiracle.LibTiff.Classic.TiffTag.COPYRIGHT, TextFor(metadata, "Copyright"));

    if (metadata.XmpPacket is { Length: > 0 } xmp)
      tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.XMLPACKET, xmp.Length, xmp);

    if (metadata.IccProfile is { Length: > 0 } icc)
      tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.ICCPROFILE, icc.Length, icc);

    if (metadata.Iptc != null) {
      var iptc = IptcCodec.ToPhotoshopSegment(metadata.Iptc);
      if (iptc.Length > 0)
        tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.RICHTIFFIPTC, iptc.Length, iptc);
    }

    // Both or neither: a file stating one density and not the other says nothing usable, and the
    // unit belongs with them or the numbers are read as a ratio.
    if (metadata.DpiX is > 0 and var x && metadata.DpiY is > 0 and var y) {
      tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.RESOLUTIONUNIT, BitMiracle.LibTiff.Classic.ResUnit.INCH);
      tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.XRESOLUTION, x);
      tiff.SetField(BitMiracle.LibTiff.Classic.TiffTag.YRESOLUTION, y);
    }
  }

  private static void _SetText(BitMiracle.LibTiff.Classic.Tiff tiff, BitMiracle.LibTiff.Classic.TiffTag tag, string? value) {
    if (!string.IsNullOrEmpty(value))
      tiff.SetField(tag, value);
  }
}
