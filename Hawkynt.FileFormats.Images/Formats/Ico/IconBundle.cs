using System.Collections.Generic;

namespace FileFormat.Ico;

/// <summary>One entry of an icon or cursor file, as the directory describes it.</summary>
/// <param name="Index">The entry's position in the directory.</param>
/// <param name="Width">The picture's width in pixels.</param>
/// <param name="Height">The picture's height in pixels.</param>
/// <param name="BitsPerPixel">The depth, taken from the payload where the payload states one.</param>
/// <param name="HotspotX">The point the pointer points at, across. Nought in an icon.</param>
/// <param name="HotspotY">The point the pointer points at, down. Nought in an icon.</param>
/// <param name="Format">Whether the payload is a PNG or a bitmap.</param>
/// <param name="Data">The payload exactly as it sits in the file.</param>
public sealed record IconBundleEntry(
  int Index,
  int Width,
  int Height,
  int BitsPerPixel,
  ushort HotspotX,
  ushort HotspotY,
  IcoImageFormat Format,
  byte[] Data
);

/// <summary>
/// An icon or cursor file read without being told in advance which of the two it is.
/// </summary>
/// <remarks>
/// The two formats are the same file but for the type field and the meaning of two bytes an entry
/// carries, so a reader that must be told which it is holding cannot open the other one at all —
/// and a cursor turns up wherever an icon does, most obviously as every frame of an animated
/// cursor. This is the shape that covers both: the type is reported rather than required, and the
/// hotspot is read where a cursor puts it and is nought where an icon does not have one.
/// </remarks>
/// <param name="Kind">Whether the file called itself an icon or a cursor.</param>
/// <param name="Entries">The entries the directory lists, in directory order.</param>
public sealed record IconBundle(
  IcoFileType Kind,
  IReadOnlyList<IconBundleEntry> Entries
);
