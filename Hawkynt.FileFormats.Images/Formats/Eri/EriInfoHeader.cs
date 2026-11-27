using System;
using System.Buffers.Binary;

namespace FileFormat.Eri;

/// <summary>The "ImageInf" record: how an ERI picture is shaped and coded.</summary>
/// <param name="Version">00020100H for the standard formats, 00020200H where the extended ones may be used, 00020300H in front of the ERISA library's lossy pictures.</param>
/// <param name="Transformation">03020000H lossless, 00000001H DCT, 00000005H LOT.</param>
/// <param name="Architecture">The entropy code: FFFFFFFFH run-length gamma, FFFFFFFCH ERINA, FFFFFFF0H ERISA, 32 arithmetic.</param>
/// <param name="FormatType">The colour type with the palette, clipping and alpha flags in the top byte.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels; negative when the rows are stored top row first.</param>
/// <param name="BitsPerPixel">8, 16, 24 or 32.</param>
/// <param name="ClippedPixel">The palette index drawn as transparent when clipping is on.</param>
/// <param name="SamplingFlags">The chroma sampling of a lossy picture.</param>
/// <param name="QuantumizedBits">Unused by any published coding; kept as read.</param>
/// <param name="AllottedBits">Unused by any published coding; kept as read.</param>
/// <param name="BlockingDegree">The block size as a power of two.</param>
/// <param name="LappedBlock">Unused by any published coding; kept as read.</param>
/// <param name="FrameTransform">00010000H when frames are stored as differences.</param>
/// <param name="FrameDegree">Unused by any published coding; kept as read.</param>
public readonly record struct EriInfoHeader(
  uint Version,
  uint Transformation,
  uint Architecture,
  uint FormatType,
  int Width,
  int Height,
  int BitsPerPixel,
  int ClippedPixel,
  uint SamplingFlags,
  ulong QuantumizedBits,
  ulong AllottedBits,
  int BlockingDegree,
  uint LappedBlock,
  uint FrameTransform,
  uint FrameDegree) {

  /// <summary>The size of the record's body.</summary>
  public const int Size = 0x44;

  public const uint StandardVersion = 0x00020100;
  public const uint ExtendedVersion = 0x00020200;
  public const uint LossyVersion = 0x00020300;

  public const uint LosslessTransformation = 0x03020000;
  public const uint DctTransformation = 0x00000001;
  public const uint LotTransformation = 0x00000005;

  public const uint RunLengthGamma = 0xFFFFFFFF;
  public const uint RunLengthHuffman = 0xFFFFFFFC;
  public const uint Nemesis = 0xFFFFFFF0;
  public const uint ArithmeticCode = 32;

  public const uint RgbImage = 0x00000001;
  public const uint GrayImage = 0x00000002;
  public const uint RgbaImage = 0x04000001;
  public const uint TypeMask = 0x00FFFFFF;
  public const uint WithPalette = 0x01000000;
  public const uint UseClipping = 0x02000000;
  public const uint WithAlpha = 0x04000000;

  /// <summary>Whether rows are stored top row first.</summary>
  public bool TopDown => this.Height < 0;

  /// <summary>The number of rows.</summary>
  public int Rows => Math.Abs(this.Height);

  internal static EriInfoHeader Parse(ReadOnlySpan<byte> body) => new(
    BinaryPrimitives.ReadUInt32LittleEndian(body),
    BinaryPrimitives.ReadUInt32LittleEndian(body[4..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[8..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[12..]),
    BinaryPrimitives.ReadInt32LittleEndian(body[16..]),
    BinaryPrimitives.ReadInt32LittleEndian(body[20..]),
    BinaryPrimitives.ReadInt32LittleEndian(body[24..]),
    BinaryPrimitives.ReadInt32LittleEndian(body[28..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[32..]),
    BinaryPrimitives.ReadUInt64LittleEndian(body[36..]),
    BinaryPrimitives.ReadUInt64LittleEndian(body[44..]),
    BinaryPrimitives.ReadInt32LittleEndian(body[52..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[56..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[60..]),
    BinaryPrimitives.ReadUInt32LittleEndian(body[64..]));

  internal void Write(Span<byte> body) {
    BinaryPrimitives.WriteUInt32LittleEndian(body, this.Version);
    BinaryPrimitives.WriteUInt32LittleEndian(body[4..], this.Transformation);
    BinaryPrimitives.WriteUInt32LittleEndian(body[8..], this.Architecture);
    BinaryPrimitives.WriteUInt32LittleEndian(body[12..], this.FormatType);
    BinaryPrimitives.WriteInt32LittleEndian(body[16..], this.Width);
    BinaryPrimitives.WriteInt32LittleEndian(body[20..], this.Height);
    BinaryPrimitives.WriteInt32LittleEndian(body[24..], this.BitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(body[28..], this.ClippedPixel);
    BinaryPrimitives.WriteUInt32LittleEndian(body[32..], this.SamplingFlags);
    BinaryPrimitives.WriteUInt64LittleEndian(body[36..], this.QuantumizedBits);
    BinaryPrimitives.WriteUInt64LittleEndian(body[44..], this.AllottedBits);
    BinaryPrimitives.WriteInt32LittleEndian(body[52..], this.BlockingDegree);
    BinaryPrimitives.WriteUInt32LittleEndian(body[56..], this.LappedBlock);
    BinaryPrimitives.WriteUInt32LittleEndian(body[60..], this.FrameTransform);
    BinaryPrimitives.WriteUInt32LittleEndian(body[64..], this.FrameDegree);
  }
}
