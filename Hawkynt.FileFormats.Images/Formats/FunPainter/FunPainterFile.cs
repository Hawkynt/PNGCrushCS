using System;
using FileFormat.Core;

namespace FileFormat.FunPainter;

/// <summary>In-memory representation of a Fun Painter II picture (.fp2, .fun) for the Commodore 64.</summary>
/// <remarks>
/// Two multicolour FLI screens shown on alternate television fields, the second displaced a pixel
/// from the first — the same interlaced arrangement GunPaint uses, and for the same reason. FLI
/// switches the video matrix on every scanline rather than every eight, so each row of a character
/// cell names its own pair of colours; interlacing two such screens lets the eye mix them, which is
/// how a machine with sixteen colours shows a hundred and thirty-five.
/// <para/>
/// The raster work costs the leftmost three character cells, which is why the picture is 296 pixels
/// wide rather than 320. Unlike GunPaint there is no background register anywhere in the file:
/// pattern 00 is always black.
/// <para/>
/// The payload may be stored run-length encoded, and usually is — the byte at offset 16 says which,
/// and the one after it is the escape value. That packing is why this format read as noise until it
/// was undone: the older reader treated the packed bytes as a bitmap.
/// </remarks>
public readonly record struct FunPainterFile
  : IImageFormatReader<FunPainterFile>, IImageToRawImage<FunPainterFile>,
    IImageFromRawImage<FunPainterFile>, IImageFormatWriter<FunPainterFile> {

  /// <summary>Displayed width; the raster work costs the leftmost cells.</summary>
  public const int Width = 296;

  /// <summary>Displayed height.</summary>
  public const int Height = 200;

  /// <summary>Character cells a stored row spans.</summary>
  public const int StrideColumns = 40;

  /// <summary>Distance between one FLI video matrix and the next.</summary>
  public const int MatrixStride = 1024;

  /// <summary>Character cells actually shown, the leftmost three being lost to the raster switch.</summary>
  public const int VisibleColumns = Width / 8;

  /// <summary>Size of an unpacked picture, load address included.</summary>
  public const int FileSize = 33694;

  /// <summary>The text every Fun Painter file carries, immediately after the load address.</summary>
  public const string Signature = "FUNPAINT (MT) ";

  /// <summary>Offset of <see cref="Signature"/>.</summary>
  public const int SignatureOffset = 2;

  /// <summary>Offset of the byte saying whether the payload is packed; nought means it is not.</summary>
  public const int PackedFlagOffset = 16;

  /// <summary>Offset of the escape value the packing uses.</summary>
  public const int EscapeOffset = 17;

  /// <summary>Offset the packed payload starts at, and the first byte the unpacking fills.</summary>
  public const int PayloadOffset = 18;

  /// <summary>Offset of the first field's video matrices.</summary>
  public const int FirstMatrixOffset = 18 + 3;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstBitmapOffset = 8210 + 24;

  /// <summary>Offset of the colour memory, which both fields share.</summary>
  public const int ColorRamOffset = 16402 + 3;

  /// <summary>Offset of the second field's video matrices.</summary>
  public const int SecondMatrixOffset = 17402 + 3;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondBitmapOffset = 25594 + 24;

  /// <summary>How far the second field sits from the first.</summary>
  public const int SecondFieldShift = 1;

  /// <summary>The load address a Fun Painter picture carries.</summary>
  public const ushort DefaultLoadAddress = 0x4000;

  static string IImageFormatMetadata<FunPainterFile>.PrimaryExtension => ".fp2";
  static string[] IImageFormatMetadata<FunPainterFile>.FileExtensions => [".fp2", ".fun"];
  static FunPainterFile IImageFormatReader<FunPainterFile>.FromSpan(ReadOnlySpan<byte> data) => FunPainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<FunPainterFile>.ToBytes(FunPainterFile file) => FunPainterWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FunPainterFile>.VideoModes => [
    new("Fun Painter II", [(Width, Height)], [Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount])
  ];

  /// <summary>
  /// The unpacked file, kept whole because every area of it is at an absolute offset. A packed file
  /// is expanded when it is read, so this is always <see cref="FileSize"/> bytes.
  /// </summary>
  public byte[] Data { get; init; }

  /// <summary>Whether the payload was, or is to be, run-length encoded.</summary>
  /// <remarks>
  /// Kept so a file read packed is written packed. The picture is the same either way — the
  /// reference decoder takes both — but a file that arrived a third of its size should not silently
  /// triple on the way out.
  /// </remarks>
  public bool Packed { get; init; }

  /// <summary>The C64 memory address the picture loads at.</summary>
  public ushort LoadAddress => this.Data is { Length: >= 2 } d ? (ushort)(d[0] | (d[1] << 8)) : (ushort)0;

  /// <summary>Renders the two fields and averages them, which is what the display does.</summary>
  public static RawImage ToRawImage(FunPainterFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _RenderField(data, FirstBitmapOffset, FirstMatrixOffset, 0, palette);
    var second = _RenderField(data, SecondBitmapOffset, SecondMatrixOffset, SecondFieldShift, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _RenderField(
    ReadOnlySpan<byte> data, int bitmap, int matrixBase, int shift, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y) {
      // FLI: the row within the cell picks which of the eight matrices applies.
      var matrix = matrixBase + ((y & 7) * MatrixStride);

      for (var x = 0; x < Width; ++x) {
        var source = x - shift;
        // The displaced field has nothing to show in the column that falls off the left.
        var index = source < 0 ? 0 : _ColorAt(data, bitmap, matrix, source, y);
        var entry = index * 3;
        var target = (y * Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return rgb;
  }

  private static byte _ColorAt(ReadOnlySpan<byte> data, int bitmap, int matrix, int x, int y) {
    var cell = (y >> 3) * StrideColumns + (x >> 3);
    var pattern = (_At(data, bitmap + (cell << 3) + (y & 7)) >> (~x & 6)) & 3;

    return (byte)(pattern switch {
      1 => _At(data, matrix + cell) >> 4,
      2 => _At(data, matrix + cell) & 15,
      3 => _At(data, ColorRamOffset + cell) & 15,
      _ => 0,
    });
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Encodes a picture as the two displaced FLI screens the format shows.</summary>
  /// <remarks>
  /// A picture the format can hold is reproduced exactly, which is what makes reading a file and
  /// writing it back lossless; anything else is approximated. Use
  /// <see cref="FromRawImageExact"/> where an approximation would be worse than a refusal.
  /// </remarks>
  public static FunPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Data = FunPainterEncoder.Encode(image.SampleTo(Width, Height).PixelData),
      Packed = true,
    };
  }

  /// <summary>Encodes a picture the format can hold exactly, and refuses one it cannot.</summary>
  /// <exception cref="ArgumentException">
  /// The picture is not <see cref="Width"/> by <see cref="Height"/>, or names a colour the two
  /// fields cannot blend, or asks a character cell for more colours than it has.
  /// </exception>
  public static FunPainterFile FromRawImageExact(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException(
        $"A Fun Painter picture is {Width}x{Height}, but this one is {image.Width}x{image.Height}.",
        nameof(image));

    return new() {
      Data = FunPainterEncoder.EncodeExact(image.EnsureFormat(PixelFormat.Rgb24).PixelData),
      Packed = true,
    };
  }
}
