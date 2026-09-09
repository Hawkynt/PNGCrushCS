using System;
using FileFormat.Core;

namespace FileFormat.HiresManager;

/// <summary>In-memory representation of a Hires Manager picture for the Commodore 64.</summary>
/// <remarks>
/// A high-resolution FLI screen of 192 rows, saved as the whole sixteen-kilobyte bank it lived in:
/// the file is always 16385 bytes and the length is part of what identifies it. The bitmap sits 320
/// bytes into the bank and the eight video matrices 8232 bytes in, a page apart because that is the
/// granularity of the VIC-II's matrix pointer — the eighth of them running into the end of the bank,
/// which it can do because 192 rows need only 960 of its thousand entries.
/// <para/>
/// The first four bytes are not picture. Two are the load address, which is always $4000, and the
/// fourth says whether what follows is packed: $FF means it is not, and anything else is a pointer
/// to the end of a run-length stream. A file that leaves it out is read as a packed one and refused.
/// <para/>
/// The old model had a 320 by 200 screen with the matrices packed a thousand bytes apart, which is
/// neither the geometry nor the layout.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct HiresManagerFile
  : IImageFormatReader<HiresManagerFile>, IImageToRawImage<HiresManagerFile>,
    IImageFromRawImage<HiresManagerFile>, IImageFormatWriter<HiresManagerFile> {

  static string IImageFormatMetadata<HiresManagerFile>.PrimaryExtension => ".him";
  static string[] IImageFormatMetadata<HiresManagerFile>.FileExtensions => [".him"];
  static HiresManagerFile IImageFormatReader<HiresManagerFile>.FromSpan(ReadOnlySpan<byte> data) => HiresManagerReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiresManagerFile>.ToBytes(HiresManagerFile file) => HiresManagerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HiresManagerFile>.VideoModes => [
    new("Hires Manager", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 192;

  /// <summary>Character rows the picture takes.</summary>
  internal const int CellRows = FixedHeight / Commodore64Graphics.CellHeight;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the byte that says the picture is not packed sits.</summary>
  internal const int PackingFlagOffset = 3;

  /// <summary>What that byte says when the picture is stored as it is.</summary>
  internal const byte NotPacked = 0xFF;

  /// <summary>Where the bitmap starts.</summary>
  internal const int BitmapOffset = 322;

  /// <summary>Bytes the bitmap takes for the rows this format shows.</summary>
  internal const int BitmapSize = CellRows * Commodore64Graphics.Columns * Commodore64Graphics.CellHeight;

  /// <summary>Where the video matrices start.</summary>
  internal const int MatricesOffset = 8234;

  /// <summary>Entries one matrix needs for the rows this format shows.</summary>
  internal const int MatrixEntries = CellRows * Commodore64Graphics.Columns;

  /// <summary>The length of a whole Hires Manager picture, which is also what identifies it.</summary>
  public const int FileSize = 16385;

  /// <summary>Load address, which is fixed: the reference decoder turns down anything else.</summary>
  internal const ushort FixedLoadAddress = 0x4000;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, always $4000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The bitmap, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(HiresManagerFile file)
    => Commodore64Fli.DecodeHires(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride, FixedHeight);

  /// <summary>Encodes a picture as a Hires Manager screen, scaling it to 296x192 first.</summary>
  public static HiresManagerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    Commodore64Fli.EncodeHires(image, FixedHeight, 0, bitmap, matrices, Commodore64Fli.MatrixStride);

    return new() { LoadAddress = FixedLoadAddress, BitmapData = bitmap, Matrices = matrices };
  }
}
