using System;
using FileFormat.Core;

namespace FileFormat.Flimatic;

/// <summary>In-memory representation of a Flimatic multicolour picture for the Commodore 64.</summary>
/// <remarks>
/// The standard FLI arrangement — colour memory, the eight video matrices a page apart, then the
/// bitmap — with one addition of its own: a background register in the trailer, so pattern 00 shows
/// a colour the picture chose rather than black. The length carries that trailer and is what tells a
/// decoder it is looking at a Flimatic and not at any of the other pictures with this shape.
/// <para/>
/// This one was the most misleading of the family to be caught by. What the writer produced was
/// 17002 bytes laid out bitmap first with the matrices packed a thousand apart, and the reference
/// decoder appeared to read it — but only because a length it does not recognise falls through to a
/// run-length path, and unpacking arbitrary bytes as if they were compressed happened to produce a
/// picture rather than an error. The apparent success was that accident. At the length the format
/// actually has, the direct path runs.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct FlimaticFile
  : IImageFormatReader<FlimaticFile>, IImageToRawImage<FlimaticFile>,
    IImageFromRawImage<FlimaticFile>, IImageFormatWriter<FlimaticFile> {

  static string IImageFormatMetadata<FlimaticFile>.PrimaryExtension => ".flm";
  static string[] IImageFormatMetadata<FlimaticFile>.FileExtensions => [".flm"];
  static FlimaticFile IImageFormatReader<FlimaticFile>.FromSpan(ReadOnlySpan<byte> data) => FlimaticReader.FromSpan(data);
  static byte[] IImageFormatWriter<FlimaticFile>.ToBytes(FlimaticFile file) => FlimaticWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FlimaticFile>.VideoModes => [
    new("Flimatic", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int FixedWidth = Commodore64Fli.VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = Commodore64Fli.ScreenHeight;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where colour memory starts: straight after the load address.</summary>
  internal const int ColorRamOffset = LoadAddressSize;

  /// <summary>Where the video matrices start, colour memory having been given a whole page.</summary>
  internal const int MatricesOffset = ColorRamOffset + Commodore64Fli.MatrixStride;

  /// <summary>Where the bitmap starts: after all eight matrices.</summary>
  internal const int BitmapOffset = MatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>Where the background register sits, well into the trailer.</summary>
  internal const int BackgroundOffset = 17281;

  /// <summary>The length of a whole Flimatic picture, which is also what identifies it.</summary>
  public const int FileSize = 17410;

  /// <summary>The last byte of the picture proper; the rest is the trailer the register lives in.</summary>
  internal const int PictureSize = BitmapOffset + Commodore64Fli.BitmapSize;

  /// <summary>Default load address, which puts the matrices at the foot of the bank at $4000.</summary>
  internal const ushort DefaultLoadAddress = 0x3C00;

  /// <summary>Image width, always 296.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Colour memory, one entry a cell, which pattern 11 takes.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>The bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The colour pattern 00 shows across the whole picture.</summary>
  public byte Background { get; init; }

  /// <summary>Whatever else the trailer held, the background register included.</summary>
  public byte[] Trailer { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(FlimaticFile file)
    => Commodore64Fli.DecodeMulticolor(
      file.BitmapData ?? [], file.Matrices ?? [], Commodore64Fli.MatrixStride,
      file.ColorRam ?? [], [file.Background], FixedHeight);

  /// <summary>Encodes a picture as Flimatic, scaling it to 296x200 first.</summary>
  /// <remarks>
  /// The background register goes to black. Choosing the commonest colour instead would be a better
  /// picture and a worse round trip, and this format has three colours a cell to spend besides it.
  /// </remarks>
  public static FlimaticFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[Commodore64Fli.BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    var colorRam = new byte[Commodore64Fli.ColorRamSize];
    Commodore64Fli.EncodeMulticolor(
      image, FixedHeight, 0, 0, bitmap, matrices, Commodore64Fli.MatrixStride, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      ColorRam = colorRam,
      Matrices = matrices,
      BitmapData = bitmap,
      Background = 0,
      Trailer = new byte[FileSize - PictureSize],
    };
  }
}
