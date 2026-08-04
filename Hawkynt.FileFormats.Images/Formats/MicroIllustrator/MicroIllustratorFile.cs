using System;
using FileFormat.Core;

namespace FileFormat.MicroIllustrator;

/// <summary>In-memory representation of a Commodore 64 Micro Illustrator multicolor image.</summary>
public readonly record struct MicroIllustratorFile : IImageFormatReader<MicroIllustratorFile>, IImageToRawImage<MicroIllustratorFile>, IImageFromRawImage<MicroIllustratorFile>, IImageFormatWriter<MicroIllustratorFile> {

  static string IImageFormatMetadata<MicroIllustratorFile>.PrimaryExtension => ".mil";
  static string[] IImageFormatMetadata<MicroIllustratorFile>.FileExtensions => [".mil"];
  static MicroIllustratorFile IImageFormatReader<MicroIllustratorFile>.FromSpan(ReadOnlySpan<byte> data) => MicroIllustratorReader.FromSpan(data);
  static byte[] IImageFormatWriter<MicroIllustratorFile>.ToBytes(MicroIllustratorFile file) => MicroIllustratorWriter.ToBytes(file);

  /// <summary>The fixed width of a Micro Illustrator image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Micro Illustrator image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The total file size in bytes: 2 + 20 + 1000 + 1000 + 8000.</summary>
  public const int ExpectedFileSize = 10022;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>The header that follows the load address and states where the picture begins.</summary>
  internal const int HeaderSize = 20;

  /// <summary>Where the header states its own length.</summary>
  internal const int HeaderSizeOffset = 6;

  /// <summary>Where the picture starts: the last ten thousand bytes of the file.</summary>
  internal const int PictureOffset = ExpectedFileSize - (VideoMatrixSize + ColorRamSize + BitmapDataSize);

  /// <summary>
  /// Where the shared background register sits.
  /// </summary>
  /// <remarks>
  /// Found by changing one header byte at a time and asking RECOIL what colour it drew behind the
  /// picture: this is the only byte that moves it.
  /// </remarks>
  internal const int BackgroundOffset = 8;

  /// <summary>Where the header states the size of each section, matrix first and bitmap last.</summary>
  internal const int SectionSizesOffset = 9;

  /// <summary>The address Micro Illustrator's own files carry.</summary>
  internal const ushort DefaultLoadAddress = 0x18DC;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, typically 0x6000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Micro Illustrator image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MicroIllustratorFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Reduces a picture to the multicolour screen this program saved.</summary>
  public static MicroIllustratorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var videoMatrix = new byte[VideoMatrixSize];
    var colorRam = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, videoMatrix, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      BitmapData = bitmap,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = background,
    };
  }

}
