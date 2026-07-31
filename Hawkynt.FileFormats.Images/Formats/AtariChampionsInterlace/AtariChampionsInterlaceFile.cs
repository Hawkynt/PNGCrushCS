using System;
using FileFormat.Core;

namespace FileFormat.AtariChampionsInterlace;

/// <summary>In-memory representation of a Champions' Interlace picture (.cin, .cci).</summary>
/// <remarks>
/// Two interlaced fields built a scanline at a time rather than a screen at a time: the Graphics 15
/// rows alternate between the two fields as they are stored, so consecutive bytes in the file are
/// consecutive rows of the picture and not of either field. The Graphics 11 hue rows then fill in
/// what each field is missing.
/// <para/>
/// The longest form gives every scanline its own four colour registers, stored as four planes of
/// 256 bytes so that one register's values down the whole screen are contiguous — which is the
/// order a display routine rewriting one register per line wants to read them in.
/// </remarks>
public readonly record struct AtariChampionsInterlaceFile
  : IImageFormatReader<AtariChampionsInterlaceFile>, IImageToRawImage<AtariChampionsInterlaceFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Bytes one row of one field occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Bytes a row of hues occupies across both fields.</summary>
  public const int HueStride = Stride * 2;

  /// <summary>Size of a file with no colours of its own.</summary>
  public const int BareSize = 15360;

  /// <summary>Size of a file with one set of registers for the whole picture.</summary>
  public const int OneSetSize = 16004;

  /// <summary>Size of a file with a set of registers per scanline.</summary>
  public const int PerRowSize = 16384;

  /// <summary>The text a compressed file starts with.</summary>
  public const string CompressedSignature = "CIN 1.2 ";

  /// <summary>The registers a file with none of its own falls back to.</summary>
  public static ReadOnlySpan<byte> DefaultRegisters => [0, 4, 8, 12];

  static string IImageFormatMetadata<AtariChampionsInterlaceFile>.PrimaryExtension => ".cin";
  static string[] IImageFormatMetadata<AtariChampionsInterlaceFile>.FileExtensions => [".cin", ".cci"];
  static AtariChampionsInterlaceFile IImageFormatReader<AtariChampionsInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariChampionsInterlaceReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariChampionsInterlaceFile>.VideoModes => [
    new("Champions' Interlace", [(Width, 192), (Width, 200)], [256])
  ];

  /// <summary>The picture, unpacked if it was compressed.</summary>
  public byte[] Data { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(AtariChampionsInterlaceFile file) {
    var data = file.Data ?? [];
    var height = file.Height;

    var first = new byte[Width * height];
    var second = new byte[Width * height];

    for (var y = 0; y < height; ++y) {
      // A file long enough gives every scanline its own registers, one plane per register.
      var registers = data.Length == PerRowSize
        ? _RowRegisters(data, BareSize + y)
        : data.Length == OneSetSize ? data.AsSpan(OneSetSize - 4, 4).ToArray() : DefaultRegisters.ToArray();

      // Consecutive stored rows belong to alternate fields.
      Atari8BitGraphics.DecodeGr15Into(
        data, y * Stride, Stride, (y & 1) == 0 ? first : second, y * Width, Width, Width, 1, registers);
    }

    Atari8BitGraphics.BlendGr11Into(data, Stride * height + Stride, HueStride, first, Width, height, 1);
    Atari8BitGraphics.BlendGr11Into(data, Stride * height, HueStride, second, Width, height, 0);

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(
        Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  /// <summary>
  /// Reads one scanline's registers, which sit 256 bytes apart so that each register's values run
  /// contiguously down the screen.
  /// </summary>
  private static byte[] _RowRegisters(ReadOnlySpan<byte> data, int offset) {
    var registers = new byte[Atari8BitGraphics.Gr15RegisterCount];
    for (var i = 0; i < registers.Length; ++i) {
      var at = offset + i * 256;
      registers[i] = at < data.Length ? data[at] : (byte)0;
    }

    return registers;
  }
}
