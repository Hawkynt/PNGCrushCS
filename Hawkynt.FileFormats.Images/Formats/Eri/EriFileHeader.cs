using System;
using System.Buffers.Binary;

namespace FileFormat.Eri;

/// <summary>The "FileHdr " record: what an ERI file contains as a whole.</summary>
/// <param name="Version">00020100H.</param>
/// <param name="ContainFlags">1 for a picture, 11H with a palette, 200H set when a sequence table is present.</param>
/// <param name="KeyFrameCount">1 when every frame stands alone; n when every n-th one does and the rest are differences; 0 when only the first does.</param>
/// <param name="FrameCount">How many frames the file holds.</param>
/// <param name="TotalTime">The length of an animation in milliseconds.</param>
public readonly record struct EriFileHeader(uint Version, uint ContainFlags, uint KeyFrameCount, uint FrameCount, uint TotalTime) {

  /// <summary>The size of the record's body.</summary>
  public const int Size = 20;

  public const uint ContainImage = 0x00000001;
  public const uint ContainPalette = 0x00000010;
  public const uint ContainSequence = 0x00000200;

  internal static EriFileHeader Parse(ReadOnlySpan<byte> body) => new(
    BinaryPrimitives.ReadUInt32LittleEndian(body),
    BinaryPrimitives.ReadUInt32LittleEndian(body[4..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[8..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[12..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[16..]));

  internal void Write(Span<byte> body) {
    BinaryPrimitives.WriteUInt32LittleEndian(body, this.Version);
    BinaryPrimitives.WriteUInt32LittleEndian(body[4..], this.ContainFlags);
    BinaryPrimitives.WriteUInt32LittleEndian(body[8..], this.KeyFrameCount);
    BinaryPrimitives.WriteUInt32LittleEndian(body[12..], this.FrameCount);
    BinaryPrimitives.WriteUInt32LittleEndian(body[16..], this.TotalTime);
  }
}

/// <summary>One entry of the "Sequence" record: which frame to show, and for how many frame periods.</summary>
public readonly record struct EriSequenceEntry(uint Frame, uint Duration);

/// <summary>How the colour of an ERI picture is held.</summary>
public enum EriColorType {

  /// <summary>Blue, green and red: 24 or 32 bits, or 5 bits each in 16.</summary>
  Rgb,

  /// <summary>Blue, green, red and opacity, 32 bits.</summary>
  Rgba,

  /// <summary>One 8-bit grey level.</summary>
  Gray,

  /// <summary>One 8-bit index into a 256-entry palette.</summary>
  Palette,
}
