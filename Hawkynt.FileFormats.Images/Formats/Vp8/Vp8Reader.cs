using System;
using System.IO;
using FileFormat.WebP;

namespace FileFormat.Vp8;

internal static class Vp8Reader {

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Vp8FrameHeader.StructSize)
      return null;

    var frame = Vp8FrameHeader.ReadFrom(header);
    return frame.IsKeyframe && frame.HasValidSignature && frame.Width > 0 && frame.Height > 0
      ? true
      : null;
  }

  public static Vp8File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Vp8FrameHeader.StructSize)
      throw new InvalidDataException($"VP8 key frame is shorter than its {Vp8FrameHeader.StructSize}-byte uncompressed header.");

    var frame = Vp8FrameHeader.ReadFrom(data);
    if (!frame.IsKeyframe)
      throw new InvalidDataException("A standalone VP8 image must start with a key frame.");
    if (!frame.HasValidSignature)
      throw new InvalidDataException("Invalid VP8 key-frame start code; expected 0x9D 0x01 0x2A.");

    var version = (frame.FrameTag0 >> 1) & 0x07;
    if (version > 3)
      throw new NotSupportedException($"VP8 version {version} is reserved for a future bitstream variant; versions 0 through 3 are supported.");
    if (frame.Width == 0 || frame.Height == 0)
      throw new InvalidDataException($"VP8 states an invalid {frame.Width}x{frame.Height} picture.");

    var tag = (uint)(data[0] | data[1] << 8 | data[2] << 16);
    var firstPartitionLength = checked((int)(tag >> 5));
    if (firstPartitionLength > data.Length - Vp8FrameHeader.StructSize)
      throw new InvalidDataException(
        $"VP8 first partition states {firstPartitionLength} bytes, but only {data.Length - Vp8FrameHeader.StructSize} remain after the key-frame header.");

    return new() {
      Width = frame.Width,
      Height = frame.Height,
      Bitstream = data.ToArray(),
    };
  }
}
