using System;
using FileFormat.Core;

namespace FileFormat.InterlaceGraphicsEditor;

/// <summary>In-memory representation of an Interlace Graphics Editor picture (.ige).</summary>
/// <remarks>
/// Two Graphics 15 screens shown on alternate television fields with their own sets of colour
/// registers. Each screen offers four colours; alternating between two such sets and letting the
/// eye average them reaches ten distinct shades where the mode alone shows four, at the cost of the
/// flicker that gives the technique its name.
/// </remarks>
[FormatMagicBytes([0xFF, 0xFF, 0xF6, 0xA3, 0xFF, 0xBB, 0xFF, 0x5F])]
public readonly record struct InterlaceGraphicsEditorFile
  : IImageFormatReader<InterlaceGraphicsEditorFile>, IImageToRawImage<InterlaceGraphicsEditorFile>,
    IImageFromRawImage<InterlaceGraphicsEditorFile>, IImageFormatWriter<InterlaceGraphicsEditorFile> {

  static byte[] IImageFormatWriter<InterlaceGraphicsEditorFile>.ToBytes(InterlaceGraphicsEditorFile file)
    => InterlaceGraphicsEditorWriter.ToBytes(file);

  /// <summary>Screen pixels across; each of the 128 logical pixels is drawn two wide.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 96;

  /// <summary>Bytes one row occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Colour registers each field carries: background, PF0, PF1 and PF2.</summary>
  public const int RegisterCount = 4;

  /// <summary>
  /// The eight bytes every file starts with — a load header rather than a signature, but a fixed
  /// one, which is the only thing that tells this format apart from the others of its size.
  /// </summary>
  public static ReadOnlySpan<byte> Signature => [0xFF, 0xFF, 0xF6, 0xA3, 0xFF, 0xBB, 0xFF, 0x5F];

  /// <summary>Offset of the first field's registers.</summary>
  public const int FirstRegisterOffset = 8;

  /// <summary>Offset of the second field's registers.</summary>
  public const int SecondRegisterOffset = 12;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstBitmapOffset = 16;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondBitmapOffset = FirstBitmapOffset + Stride * Height;

  /// <summary>Total file size.</summary>
  public const int FileSize = SecondBitmapOffset + Stride * Height;

  static string IImageFormatMetadata<InterlaceGraphicsEditorFile>.PrimaryExtension => ".ige";
  static string[] IImageFormatMetadata<InterlaceGraphicsEditorFile>.FileExtensions => [".ige"];
  static InterlaceGraphicsEditorFile IImageFormatReader<InterlaceGraphicsEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => InterlaceGraphicsEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<InterlaceGraphicsEditorFile>.VideoModes => [
    new("Interlaced Graphics 15", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(InterlaceGraphicsEditorFile file) {
    var data = file.Data ?? [];

    var first = Atari8BitGraphics.DecodeGr15Frame(
      data, FirstBitmapOffset, Stride, Width, Height, _Registers(data, FirstRegisterOffset));
    var second = Atari8BitGraphics.DecodeGr15Frame(
      data, SecondBitmapOffset, Stride, Width, Height, _Registers(data, SecondRegisterOffset));

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Registers(ReadOnlySpan<byte> data, int offset) {
    var registers = new byte[RegisterCount];
    for (var i = 0; i < RegisterCount && offset + i < data.Length; ++i)
      registers[i] = data[offset + i];

    return registers;
  }

  /// <summary>Builds a picture with the same registers and the same field in both halves.</summary>
  /// <remarks>
  /// The two fields normally carry different colours, and averaging them is what puts more on the
  /// screen than four. A single picture gives no second set to average against, so both halves hold
  /// the same one and the result is exactly what was written.
  /// </remarks>
  public static InterlaceGraphicsEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var registers = Atari8BitGraphics.ChooseGr15Registers(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, Width * Height, RegisterCount);

    var field = Atari8BitGraphics.PackGr15Frame(rgb.PixelData, Stride, Width, Height, registers);
    var data = new byte[FileSize];
    Signature.CopyTo(data.AsSpan(0));

    registers.CopyTo(data.AsSpan(FirstRegisterOffset));
    registers.CopyTo(data.AsSpan(SecondRegisterOffset));
    field.CopyTo(data.AsSpan(FirstBitmapOffset));
    field.CopyTo(data.AsSpan(SecondBitmapOffset));

    return new() { Data = data };
  }
}
