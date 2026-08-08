using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FileFormat.Core;

/// <summary>TIFF tag data types (TIFF 6.0 §2, table 1). Values beyond what <see cref="ExifCodec"/>
/// gives typed accessors for (FLOAT/DOUBLE and anything vendor-specific) still round-trip losslessly
/// as their raw component bytes.</summary>
public enum ExifTagType : ushort {
  Byte = 1,
  Ascii = 2,
  Short = 3,
  Long = 4,
  Rational = 5,
  SByte = 6,
  Undefined = 7,
  SShort = 8,
  SLong = 9,
  SRational = 10,
  Float = 11,
  Double = 12,
}

/// <summary>An unsigned TIFF RATIONAL: two LONGs, numerator over denominator.</summary>
public readonly record struct ExifRational(uint Numerator, uint Denominator) {
  public double ToDouble() => this.Denominator == 0 ? 0 : (double)this.Numerator / this.Denominator;
  public override string ToString() => $"{this.Numerator}/{this.Denominator}";
}

/// <summary>A signed TIFF SRATIONAL.</summary>
public readonly record struct ExifSRational(int Numerator, int Denominator) {
  public double ToDouble() => this.Denominator == 0 ? 0 : (double)this.Numerator / this.Denominator;
  public override string ToString() => $"{this.Numerator}/{this.Denominator}";
}

/// <summary>
/// One TIFF/EXIF IFD entry, kept as its exact component bytes (in the owning <see cref="ExifData"/>'s
/// <see cref="ExifData.LittleEndian"/> byte order) rather than eagerly decoded — this is what lets an
/// entry of a type we don't specifically understand still round-trip byte-for-byte through
/// <see cref="ExifCodec.Write"/>.
/// </summary>
/// <param name="Tag">TIFF tag ID, e.g. 0x010F (Make) or 0x829A (ExposureTime).</param>
/// <param name="Type">Component type.</param>
/// <param name="Count">Component count (not byte count — a Count of 3 Rationals is 24 bytes).</param>
/// <param name="RawBytes">Exact component bytes, length == Count * <see cref="ExifCodec.TypeSize"/>(Type).</param>
public sealed record ExifTagEntry(ushort Tag, ExifTagType Type, int Count, byte[] RawBytes);

/// <summary>One IFD's worth of entries (IFD0, the Exif sub-IFD, or the GPS sub-IFD).</summary>
public sealed class ExifIfd {
  public IReadOnlyList<ExifTagEntry> Entries { get; init; } = [];

  public ExifTagEntry? Find(ushort tag) => this.Entries.FirstOrDefault(e => e.Tag == tag);
}

/// <summary>
/// Parsed EXIF metadata: TIFF byte order plus IFD0 and its two well-known sub-IFDs (Exif, GPS).
/// IFD1 (the thumbnail chain some encoders attach) is intentionally not preserved — see
/// <see cref="ExifCodec"/> remarks.
/// </summary>
public sealed class ExifData {
  public required bool LittleEndian { get; init; }
  public ExifIfd Ifd0 { get; init; } = new();
  public ExifIfd? ExifIfd { get; init; }
  public ExifIfd? GpsIfd { get; init; }

  // ---- common well-known tag IDs (IFD0 / Exif sub-IFD) ----
  public const ushort TagMake = 0x010F;
  public const ushort TagModel = 0x0110;
  public const ushort TagOrientation = 0x0112;
  public const ushort TagSoftware = 0x0131;
  public const ushort TagDateTime = 0x0132;
  public const ushort TagArtist = 0x013B;
  public const ushort TagCopyright = 0x8298;
  public const ushort TagExifIfdPointer = 0x8769;
  public const ushort TagGpsIfdPointer = 0x8825;
  public const ushort TagExposureTime = 0x829A;
  public const ushort TagFNumber = 0x829D;
  public const ushort TagIsoSpeed = 0x8827;
  public const ushort TagDateTimeOriginal = 0x9003;
  public const ushort TagDateTimeDigitized = 0x9004;

  // ---- GPS sub-IFD tag IDs ----
  public const ushort GpsTagLatitudeRef = 1;
  public const ushort GpsTagLatitude = 2;
  public const ushort GpsTagLongitudeRef = 3;
  public const ushort GpsTagLongitude = 4;

  /// <summary>Decodes an ASCII-typed entry's bytes to a string, trimming the mandatory trailing NUL.</summary>
  public static string DecodeAscii(ExifTagEntry entry) {
    var span = entry.RawBytes.AsSpan();
    var nul = span.IndexOf((byte)0);
    return Encoding.ASCII.GetString(nul >= 0 ? span[..nul] : span);
  }

  /// <summary>Decodes a SHORT-typed entry's first component as an unsigned 16-bit value.</summary>
  public ushort DecodeShort(ExifTagEntry entry)
    => this.LittleEndian
      ? (ushort)(entry.RawBytes[0] | (entry.RawBytes[1] << 8))
      : (ushort)((entry.RawBytes[0] << 8) | entry.RawBytes[1]);

  /// <summary>Decodes a LONG-typed entry's first component as an unsigned 32-bit value.</summary>
  public uint DecodeLong(ExifTagEntry entry) {
    var b = entry.RawBytes;
    return this.LittleEndian
      ? (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24))
      : (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
  }

  /// <summary>Decodes every RATIONAL component of an entry.</summary>
  public ExifRational[] DecodeRationals(ExifTagEntry entry) {
    var result = new ExifRational[entry.Count];
    for (var i = 0; i < entry.Count; ++i) {
      var o = i * 8;
      var num = this.LittleEndian
        ? (uint)(entry.RawBytes[o] | (entry.RawBytes[o + 1] << 8) | (entry.RawBytes[o + 2] << 16) | (entry.RawBytes[o + 3] << 24))
        : (uint)((entry.RawBytes[o] << 24) | (entry.RawBytes[o + 1] << 16) | (entry.RawBytes[o + 2] << 8) | entry.RawBytes[o + 3]);
      var den = this.LittleEndian
        ? (uint)(entry.RawBytes[o + 4] | (entry.RawBytes[o + 5] << 8) | (entry.RawBytes[o + 6] << 16) | (entry.RawBytes[o + 7] << 24))
        : (uint)((entry.RawBytes[o + 4] << 24) | (entry.RawBytes[o + 5] << 16) | (entry.RawBytes[o + 6] << 8) | entry.RawBytes[o + 7]);
      result[i] = new ExifRational(num, den);
    }

    return result;
  }

  /// <summary>Convenience lookup across IFD0 and the Exif sub-IFD (most human-facing tags live in one
  /// or the other, never both) returning the raw entry, or <c>null</c> if absent from either.</summary>
  public ExifTagEntry? FindTag(ushort tag) => this.Ifd0.Find(tag) ?? this.ExifIfd?.Find(tag);
}
