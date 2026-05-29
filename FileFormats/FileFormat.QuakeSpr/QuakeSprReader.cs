using System;
using System.IO;

namespace FileFormat.QuakeSpr;

/// <summary>Reads Quake 1 sprite (.spr) files from bytes, streams, or file paths.</summary>
public static class QuakeSprReader {

  private const uint _MAGIC = 0x50534449; // "IDSP" as LE uint32

  public static QuakeSprFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Quake sprite file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QuakeSprFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static QuakeSprFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < QuakeSprHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Quake sprite file.");

    var header = QuakeSprHeader.ReadFrom(data.Slice(0, QuakeSprHeader.StructSize));

    if (header.Magic != _MAGIC)
      throw new InvalidDataException($"Invalid Quake sprite magic: 0x{header.Magic:X8}, expected 0x{_MAGIC:X8}.");

    if (header.Version != 1)
      throw new InvalidDataException($"Unsupported Quake sprite version: {header.Version}, expected 1.");

    if (header.NumFrames < 1)
      throw new InvalidDataException($"Invalid frame count: {header.NumFrames}.");

    // Read first frame
    var offset = QuakeSprHeader.StructSize;
    if (data.Length < offset + QuakeSprFrameHeader.StructSize)
      throw new InvalidDataException("Data too small for frame header.");

    var frameHeader = QuakeSprFrameHeader.ReadFrom(data.Slice(offset, QuakeSprFrameHeader.StructSize));

    if (frameHeader.Width <= 0)
      throw new InvalidDataException($"Invalid frame width: {frameHeader.Width}.");
    if (frameHeader.Height <= 0)
      throw new InvalidDataException($"Invalid frame height: {frameHeader.Height}.");

    var pixelCount = frameHeader.Width * frameHeader.Height;
    var pixelOffset = offset + QuakeSprFrameHeader.StructSize;
    if (data.Length < pixelOffset + pixelCount)
      throw new InvalidDataException("Data too small for pixel data.");

    var pixelData = new byte[pixelCount];
    data.Slice(pixelOffset, pixelCount).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = frameHeader.Width,
      Height = frameHeader.Height,
      SpriteType = header.SpriteType,
      NumFrames = header.NumFrames,
      BoundingRadius = header.BoundingRadius,
      BeamLength = header.BeamLength,
      SyncType = header.SyncType,
      PixelData = pixelData,
    };
    }

  public static QuakeSprFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
