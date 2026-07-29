using System;
using FileFormat.Core;

namespace FileFormat.McPainter;

/// <summary>In-memory representation of a McPainter picture (.mcp) for the Atari 8-bit.</summary>
/// <remarks>
/// Two Graphics 15 pictures and two sets of colour registers. The machine shows the pictures on
/// alternate television fields, and swaps which set of registers applies on alternate scanlines
/// within each — so a scanline drawn with one set in the first field is drawn with the other in the
/// second. The eye averages the two fields, which is how a screen with four registers ends up
/// showing many more colours than four.
/// <para/>
/// The result of that averaging is what a decoder has to produce, so this decodes to RGB: the
/// blended colours are not registers and cannot be named by an index.
/// </remarks>
public readonly record struct McPainterFile
  : IImageFormatReader<McPainterFile>, IImageToRawImage<McPainterFile> {

  /// <summary>Displayed width; each of the 160 logical pixels is drawn two wide.</summary>
  public const int Width = Atari8BitGraphics.Gr15Width * 2;

  /// <summary>Scanlines.</summary>
  public const int Height = 200;

  /// <summary>Bytes one field's bitmap occupies.</summary>
  public const int FieldSize = Atari8BitGraphics.Gr15BytesPerRow * Height;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondFieldOffset = FieldSize;

  /// <summary>Offset of the colour registers: two sets of four.</summary>
  public const int ColorsOffset = FieldSize * 2;

  /// <summary>Colour registers in one set: PF0, PF1, PF2 and the background.</summary>
  public const int RegistersPerSet = Atari8BitGraphics.Gr15RegisterCount;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorsOffset + RegistersPerSet * 2;

  static string IImageFormatMetadata<McPainterFile>.PrimaryExtension => ".mcp";
  static string[] IImageFormatMetadata<McPainterFile>.FileExtensions => [".mcp"];
  static McPainterFile IImageFormatReader<McPainterFile>.FromSpan(ReadOnlySpan<byte> data) => McPainterReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<McPainterFile>.VideoModes => [
    new("McPainter", [(Width, Height)], [RegistersPerSet * 2])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(McPainterFile file) {
    var data = file.Data ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    // Set A applies to even scanlines of the first field and odd scanlines of the second; set B is
    // the other way round. That swap is the whole trick — without it both fields would be the same
    // picture and the blend would change nothing.
    var setA = _Registers(data, ColorsOffset, gtia);
    var setB = _Registers(data, ColorsOffset + RegistersPerSet, gtia);

    var first = _RenderField(data, 0, setA, setB);
    var second = _RenderField(data, SecondFieldOffset, setB, setA);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(first, second),
    };
  }

  /// <summary>Looks up one set of four registers as RGB triplets.</summary>
  private static byte[] _Registers(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> gtia) {
    var rgb = new byte[RegistersPerSet * 3];
    for (var i = 0; i < RegistersPerSet; ++i) {
      // The low bit of a colour register does not reach the screen.
      var color = (offset + i < data.Length ? data[offset + i] : 0) & 254;
      gtia.Slice(color * 3, 3).CopyTo(rgb.AsSpan(i * 3));
    }

    return rgb;
  }

  /// <summary>Renders one television field, alternating register sets by scanline.</summary>
  private static byte[] _RenderField(ReadOnlySpan<byte> data, int offset, byte[] evenRows, byte[] oddRows) {
    var rgb = new byte[Width * Height * 3];
    Span<byte> pixels = stackalloc byte[Atari8BitGraphics.Gr15Width];

    for (var y = 0; y < Height; ++y) {
      Atari8BitGraphics.UnpackGr15Row(data, offset + y * Atari8BitGraphics.Gr15BytesPerRow, pixels);
      var registers = (y & 1) == 0 ? evenRows : oddRows;

      for (var x = 0; x < Atari8BitGraphics.Gr15Width; ++x) {
        var register = Atari8BitGraphics.RegisterForGr15Pixel(pixels[x]) * 3;
        // Each logical pixel covers two screen pixels.
        for (var repeat = 0; repeat < 2; ++repeat) {
          var target = (y * Width + x * 2 + repeat) * 3;
          rgb[target] = registers[register];
          rgb[target + 1] = registers[register + 1];
          rgb[target + 2] = registers[register + 2];
        }
      }
    }

    return rgb;
  }
}
