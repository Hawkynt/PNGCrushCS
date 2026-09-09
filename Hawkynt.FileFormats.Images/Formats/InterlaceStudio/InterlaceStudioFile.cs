using System;
using FileFormat.Core;

namespace FileFormat.InterlaceStudio;

/// <summary>In-memory representation of an Interlace Studio picture for the Atari 8-bit.</summary>
/// <remarks>
/// Two ANTIC mode E screens shown one after the other fast enough that the eye adds them, which is
/// how four colours a line become a good many more. What makes this one worth the name is that the
/// four colour registers are reloaded on every raster line: the file carries four tables of 200
/// entries, one for the background and one for each playfield register, and a line takes its four
/// colours from the same position in each.
/// <para/>
/// A file is always 17184 bytes: a sixteen-byte header, the first screen, the second a whole
/// eight-kilobyte page after it rather than 8000 bytes, and the four register tables at 16384.
/// <para/>
/// This was modelled on the Commodore 64 first — bitmap, video matrix and colour memory twice over,
/// 19003 bytes — and then as a pair of grey ramps of 16208, neither of which is the format nor its
/// length. The registers are what it colours by, and they were not being written at all.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct InterlaceStudioFile
  : IImageFormatReader<InterlaceStudioFile>, IImageToRawImage<InterlaceStudioFile>,
    IImageFromRawImage<InterlaceStudioFile>, IImageFormatWriter<InterlaceStudioFile> {

  static string IImageFormatMetadata<InterlaceStudioFile>.PrimaryExtension => ".ist";
  static string[] IImageFormatMetadata<InterlaceStudioFile>.FileExtensions => [".ist"];
  static InterlaceStudioFile IImageFormatReader<InterlaceStudioFile>.FromSpan(ReadOnlySpan<byte> data) => InterlaceStudioReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterlaceStudioFile>.ToBytes(InterlaceStudioFile file) => InterlaceStudioWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<InterlaceStudioFile>.VideoModes => [
    new("Interlace Studio", [(ImageWidth, ImageHeight)])
  ];

  /// <summary>Pixels across: 160 stored, each drawn two wide.</summary>
  public const int ImageWidth = 320;

  /// <summary>Rows.</summary>
  public const int ImageHeight = 200;

  /// <summary>Bytes a row takes at two bits a stored pixel.</summary>
  internal const int BytesPerRow = Atari8BitGraphics.Gr15BytesPerRow;

  /// <summary>The bytes one screen uses.</summary>
  internal const int FrameSize = BytesPerRow * ImageHeight;

  /// <summary>The address space the first screen occupies, being a whole page.</summary>
  internal const int FrameStride = 8192;

  /// <summary>The header before the first screen.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Where the first screen starts.</summary>
  internal const int FirstFrameOffset = HeaderSize;

  /// <summary>Where the second starts: a page after the first, not 8000 bytes after it.</summary>
  internal const int SecondFrameOffset = HeaderSize + FrameStride;

  /// <summary>Where the four register tables start.</summary>
  internal const int RegistersOffset = 16384;

  /// <summary>Entries one register table holds, one for each raster line.</summary>
  internal const int RegisterTableSize = ImageHeight;

  /// <summary>Register tables: the background and the three playfield registers, in that order.</summary>
  internal const int RegisterTableCount = Atari8BitGraphics.Gr15RegisterCount;

  /// <summary>The length of a whole Interlace Studio picture, which is also what identifies it.</summary>
  public const int FileSize = RegistersOffset + RegisterTableCount * RegisterTableSize;

  /// <summary>Always 320.</summary>
  public int Width => ImageWidth;

  /// <summary>Always 200.</summary>
  public int Height => ImageHeight;

  /// <summary>The sixteen bytes before the picture.</summary>
  public byte[] Header { get; init; }

  /// <summary>The first screen, two bits a stored pixel.</summary>
  public byte[] FirstFrame { get; init; }

  /// <summary>The second screen.</summary>
  public byte[] SecondFrame { get; init; }

  /// <summary>
  /// The four register tables end to end: background, PF0, PF1 and PF2, 200 entries apiece.
  /// </summary>
  public byte[] Registers { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(InterlaceStudioFile file) {
    var first = file.FirstFrame ?? [];
    var second = file.SecondFrame ?? [];
    var registers = file.Registers ?? [];
    var firstRgb = new byte[ImageWidth * ImageHeight * 3];
    var secondRgb = new byte[ImageWidth * ImageHeight * 3];
    Span<byte> line = stackalloc byte[RegisterTableCount];

    for (var y = 0; y < ImageHeight; ++y) {
      for (var register = 0; register < RegisterTableCount; ++register) {
        var at = register * RegisterTableSize + y;
        line[register] = at < registers.Length ? registers[at] : (byte)0;
      }

      _Row(first, y, line).CopyTo(firstRgb.AsSpan(y * ImageWidth * 3));
      _Row(second, y, line).CopyTo(secondRgb.AsSpan(y * ImageWidth * 3));
    }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(firstRgb, secondRgb),
    };
  }

  private static byte[] _Row(ReadOnlySpan<byte> frame, int y, ReadOnlySpan<byte> registers)
    => Atari8BitGraphics.DecodeGr15Frame(frame, y * BytesPerRow, BytesPerRow, ImageWidth, 1, registers);

  /// <summary>Encodes a picture as an Interlace Studio pair, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// One set of four registers for the whole picture, repeated down both tables, and both screens
  /// written the same so their average is the screen itself. Choosing a different four on every
  /// raster line and then two screens whose average is nearer the original is what the format is for
  /// and is a problem of its own; this writes a correct file rather than a bad attempt at that one.
  /// </remarks>
  public static InterlaceStudioFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var scaled = image.SampleTo(ImageWidth, ImageHeight);
    var bgra = PixelConverter.Convert(scaled, PixelFormat.Bgra32).PixelData;
    var registers = Atari8BitGraphics.ChooseGr15Registers(bgra, ImageWidth * ImageHeight, RegisterTableCount);
    var frame = Atari8BitGraphics.PackGr15Frame(scaled.PixelData, BytesPerRow, ImageWidth, ImageHeight, registers);

    var tables = new byte[RegisterTableCount * RegisterTableSize];
    for (var register = 0; register < RegisterTableCount; ++register)
      tables.AsSpan(register * RegisterTableSize, RegisterTableSize).Fill((byte)(registers[register] & 254));

    return new() {
      Header = new byte[HeaderSize],
      FirstFrame = frame,
      SecondFrame = (byte[])frame.Clone(),
      Registers = tables,
    };
  }
}
