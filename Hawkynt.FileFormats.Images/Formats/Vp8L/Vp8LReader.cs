using System;
using System.IO;
using FileFormat.WebP;

namespace FileFormat.Vp8L;

internal static class Vp8LReader {

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Vp8LHeader.StructSize)
      return null;

    var lossless = Vp8LHeader.ReadFrom(header);
    return lossless.Signature == 0x2F && (lossless.BitField >> 29) == 0
      ? true
      : null;
  }

  public static Vp8LFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Vp8LHeader.StructSize)
      throw new InvalidDataException($"VP8L image is shorter than its {Vp8LHeader.StructSize}-byte header.");

    var header = Vp8LHeader.ReadFrom(data);
    if (header.Signature != 0x2F)
      throw new InvalidDataException($"Invalid VP8L signature byte 0x{header.Signature:X2}; expected 0x2F.");

    var version = header.BitField >> 29;
    if (version != 0)
      throw new InvalidDataException($"VP8L version field must be zero, but the stream states {version}.");

    return new() {
      Width = header.Width,
      Height = header.Height,
      AlphaHint = header.HasAlpha,
      Bitstream = data.ToArray(),
    };
  }
}
