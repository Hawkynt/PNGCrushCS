using System;
using FileFormat.Core;

namespace FileFormat.EmcEditor;

/// <summary>In-memory representation of an EMC-editor multicolour picture for the Commodore 64.</summary>
/// <remarks>
/// A FLI screen with the eight video matrices first, a page apart because that is the granularity of
/// the VIC-II's matrix pointer, then the bitmap, then colour memory a whole sixteen kilobytes in, and
/// a background register as the last byte of the file.
/// <para/>
/// What is shown is 192 rows and not 200, and they start at the fifth raster line of the screen
/// rather than the first — so the picture's row 0 is screen row 4, and which of the eight matrices
/// speaks for it follows from that rather than from the row's own number. Getting that wrong shifts
/// every colour half a cell up the screen while leaving the shape of the picture intact, which is
/// exactly the kind of error a round trip through one's own reader cannot see.
/// <para/>
/// It used to be written as 10000 bytes of bitmap, one video matrix and colour memory at 160 by 200,
/// which is an ordinary multicolour screen and not FLI at all.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct EmcEditorFile
  : IImageFormatReader<EmcEditorFile>, IImageToRawImage<EmcEditorFile>,
    IImageFromRawImage<EmcEditorFile>, IImageFormatWriter<EmcEditorFile> {

  static string IImageFormatMetadata<EmcEditorFile>.PrimaryExtension => ".emc";
  static string[] IImageFormatMetadata<EmcEditorFile>.FileExtensions => [".emc"];
  static EmcEditorFile IImageFormatReader<EmcEditorFile>.FromSpan(ReadOnlySpan<byte> data) => EmcEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<EmcEditorFile>.ToBytes(EmcEditorFile file) => EmcEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<EmcEditorFile>.VideoModes => [
    new("EMC-editor", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows: the screen less the half-cell at the top and the one at the bottom.</summary>
  public const int FixedHeight = 192;

  /// <summary>The raster line of the screen the picture's first row is.</summary>
  internal const int FirstRow = 4;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the video matrices start: straight after the load address.</summary>
  internal const int MatricesOffset = LoadAddressSize;

  /// <summary>Where the bitmap starts: after all eight matrices.</summary>
  internal const int BitmapOffset = MatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>Where colour memory starts, a whole sixteen kilobytes past the load address.</summary>
  internal const int ColorRamOffset = LoadAddressSize + 16384;

  /// <summary>The background register: the last byte of the file.</summary>
  internal const int BackgroundOffset = FileSize - 1;

  /// <summary>The length of a whole EMC-editor picture, which is also what identifies it.</summary>
  public const int FileSize = 17412;

  /// <summary>Default load address, which puts the matrices at the foot of the bank at $4000.</summary>
  internal const ushort DefaultLoadAddress = 0x4000;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>The bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Colour memory, one entry a cell, which pattern 11 takes.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The colour pattern 00 shows across the whole picture.</summary>
  public byte Background { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(EmcEditorFile file)
    => Commodore64Fli.DecodeMulticolor(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride,
      file.ColorRam ?? [], [file.Background], FixedHeight, FirstRow);

  /// <summary>Encodes a picture as an EMC-editor screen, scaling it to 296x192 first.</summary>
  public static EmcEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[Commodore64Fli.BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    var colorRam = new byte[Commodore64Fli.ColorRamSize];
    Commodore64Fli.EncodeMulticolor(
      image, FixedHeight, FirstRow, 0, bitmap, matrices, Commodore64Fli.MatrixStride, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      Matrices = matrices,
      BitmapData = bitmap,
      ColorRam = colorRam,
      Background = 0,
    };
  }
}
