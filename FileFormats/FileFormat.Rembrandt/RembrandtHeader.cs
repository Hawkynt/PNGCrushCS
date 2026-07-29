using System;

namespace FileFormat.Rembrandt;

/// <summary>The 216-byte header of a Rembrandt (.tcp) file.</summary>
/// <remarks>
/// Rembrandt wraps its pixels in a small chunked container. The fields below are the ones every
/// reader validates:
/// <list type="bullet">
/// <item>offset 0: the ASCII signature <c>TRUECOLR</c></item>
/// <item>offset 12: <c>00 12 00 01 00 01</c> — version/flags, fixed in practice</item>
/// <item>offset 18: the ASCII chunk tag <c>PICT</c></item>
/// <item>offset 28: width and height as big-endian 16-bit values</item>
/// </list>
/// The remaining bytes are padding that readers skip; we zero them. RGB565 big-endian pixel data
/// begins at offset 216, so a file measures <c>216 + width * height * 2</c> bytes.
/// </remarks>
public static class RembrandtHeader {

  /// <summary>ASCII signature every Rembrandt file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "TRUECOLR"u8;

  /// <summary>ASCII tag introducing the picture chunk.</summary>
  public static ReadOnlySpan<byte> PictureTag => "PICT"u8;

  /// <summary>Total header size in bytes; pixel data starts here.</summary>
  public const int StructSize = 216;

  /// <summary>Offset of the big-endian width/height pair.</summary>
  public const int DimensionsOffset = 28;

  private const int _VersionOffset = 12;
  private const int _PictureTagOffset = 18;

  /// <summary>The fixed six bytes at <see cref="_VersionOffset"/>.</summary>
  private static ReadOnlySpan<byte> _Version => [0x00, 0x12, 0x00, 0x01, 0x00, 0x01];

  /// <summary>Reads width and height; returns <c>false</c> when this is not a Rembrandt header.</summary>
  public static bool TryRead(ReadOnlySpan<byte> data, out int width, out int height) {
    width = height = 0;
    if (data.Length < StructSize)
      return false;

    if (!data[..Signature.Length].SequenceEqual(Signature))
      return false;

    if (!data.Slice(_VersionOffset, _Version.Length).SequenceEqual(_Version))
      return false;

    if (!data.Slice(_PictureTagOffset, PictureTag.Length).SequenceEqual(PictureTag))
      return false;

    width = (data[DimensionsOffset] << 8) | data[DimensionsOffset + 1];
    height = (data[DimensionsOffset + 2] << 8) | data[DimensionsOffset + 3];
    return true;
  }

  /// <summary>Writes the header into <paramref name="destination"/>, which must be at least
  /// <see cref="StructSize"/> bytes and is assumed to be zero-filled.</summary>
  public static void Write(Span<byte> destination, int width, int height) {
    Signature.CopyTo(destination);
    _Version.CopyTo(destination[_VersionOffset..]);
    PictureTag.CopyTo(destination[_PictureTagOffset..]);
    destination[DimensionsOffset] = (byte)(width >> 8);
    destination[DimensionsOffset + 1] = (byte)width;
    destination[DimensionsOffset + 2] = (byte)(height >> 8);
    destination[DimensionsOffset + 3] = (byte)height;
  }
}
