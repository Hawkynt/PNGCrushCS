using System;
using FileFormat.Core;

namespace FileFormat.HiresInterlaceFeniks;

/// <summary>In-memory representation of a Hires Interlace (.hlf) picture for the Commodore 64.</summary>
/// <remarks>
/// Two ordinary high-resolution screens shown on alternate television fields and averaged by the
/// eye, which is how sixteen colours become a hundred or so. Each is a bitmap and one video matrix;
/// there is no FLI here and no colour memory.
/// <para/>
/// The four parts are not stored in the order a description would name them. Counted from the load
/// address the first bitmap is at 0, the second screen's video matrix at $2400, the first's at
/// $2800 and the second bitmap at $4000 — the two matrices in the other order from the two bitmaps,
/// and each on a page boundary because that is what the VIC-II's matrix pointer can address. What
/// was written instead was 18002 bytes of bitmap, screen, bitmap, screen, which is the order of the
/// sentence and not of the machine.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct HiresInterlaceFeniksFile
  : IImageFormatReader<HiresInterlaceFeniksFile>, IImageToRawImage<HiresInterlaceFeniksFile>,
    IImageFromRawImage<HiresInterlaceFeniksFile>, IImageFormatWriter<HiresInterlaceFeniksFile> {

  static string IImageFormatMetadata<HiresInterlaceFeniksFile>.PrimaryExtension => ".hlf";
  static string[] IImageFormatMetadata<HiresInterlaceFeniksFile>.FileExtensions => [".hlf", ".hie"];
  static HiresInterlaceFeniksFile IImageFormatReader<HiresInterlaceFeniksFile>.FromSpan(ReadOnlySpan<byte> data) => HiresInterlaceFeniksReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiresInterlaceFeniksFile>.ToBytes(HiresInterlaceFeniksFile file) => HiresInterlaceFeniksWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HiresInterlaceFeniksFile>.VideoModes => [
    new("Hires Interlace", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of one bitmap in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of one video matrix in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Where the first field's bitmap starts.</summary>
  internal const int FirstBitmapOffset = LoadAddressSize;

  /// <summary>Where the second field's video matrix starts, which comes before the first field's.</summary>
  internal const int SecondScreenOffset = LoadAddressSize + 0x2400;

  /// <summary>Where the first field's video matrix starts.</summary>
  internal const int FirstScreenOffset = LoadAddressSize + 0x2800;

  /// <summary>Where the second field's bitmap starts.</summary>
  internal const int SecondBitmapOffset = LoadAddressSize + 0x4000;

  /// <summary>The length of a whole picture, which is also what identifies it.</summary>
  public const int FileSize = 24578;

  /// <summary>Default load address, which puts the second field's bitmap at $6000.</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The first field's bitmap.</summary>
  public byte[] FirstBitmap { get; init; }

  /// <summary>The first field's video matrix, two colours to a cell.</summary>
  public byte[] FirstScreen { get; init; }

  /// <summary>The second field's bitmap.</summary>
  public byte[] SecondBitmap { get; init; }

  /// <summary>The second field's video matrix, two colours to a cell.</summary>
  public byte[] SecondScreen { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(HiresInterlaceFeniksFile file) {
    var first = _Field(file.FirstBitmap ?? [], file.FirstScreen ?? []);
    var second = _Field(file.SecondBitmap ?? [], file.SecondScreen ?? []);

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Field(ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> screen)
    => Commodore64Graphics.DecodeHires(bitmap, screen, FixedWidth, FixedHeight).PixelData;

  /// <summary>Encodes a picture as a Hires Interlace pair, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// Both fields get identical contents, so the average of the two is the field itself. Solving for
  /// two fields whose average is nearer the original is what the format is for and is a different
  /// problem from encoding one.
  /// </remarks>
  public static HiresInterlaceFeniksFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenRamSize];
    Commodore64Graphics.EncodeHires(rgb, FixedWidth, FixedHeight, bitmap, screen);

    return new() {
      LoadAddress = DefaultLoadAddress,
      FirstBitmap = bitmap,
      FirstScreen = screen,
      SecondBitmap = (byte[])bitmap.Clone(),
      SecondScreen = (byte[])screen.Clone(),
    };
  }
}
