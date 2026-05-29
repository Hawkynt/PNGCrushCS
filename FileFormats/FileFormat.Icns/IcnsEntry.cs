using System;

namespace FileFormat.Icns;

/// <summary>Represents a single entry in an ICNS file.</summary>
/// <param name="OsType">The four-character OSType code identifying the icon type (e.g. "ic07", "is32").</param>
/// <param name="Data">The raw data for this entry (excluding the 8-byte entry header).</param>
/// <param name="Width">The pixel width of the icon, or 0 if unknown.</param>
/// <param name="Height">The pixel height of the icon, or 0 if unknown.</param>
public readonly record struct IcnsEntry(string OsType, byte[] Data, int Width, int Height) {

  /// <summary>The size of an entry header in bytes (4-byte OSType + 4-byte length).</summary>
  internal const int HeaderSize = 8;

  /// <summary>Whether this entry contains embedded PNG data (modern icon types).</summary>
  public bool IsPng => OsType is "ic07" or "ic08" or "ic09" or "ic10" or "ic11" or "ic12" or "ic13" or "ic14";

  /// <summary>Whether this entry contains legacy 24-bit RLE-compressed channel data.</summary>
  public bool IsLegacyRgb => OsType is "is32" or "il32" or "ih32" or "it32";

  /// <summary>Whether this entry is an 8-bit alpha mask for a legacy icon.</summary>
  public bool IsLegacyMask => OsType is "s8mk" or "l8mk" or "h8mk" or "t8mk";

  /// <summary>Whether this entry is a 1-bit icon with mask.</summary>
  public bool Is1Bit => OsType is "ICN#" or "icm#" or "ics#";
}
