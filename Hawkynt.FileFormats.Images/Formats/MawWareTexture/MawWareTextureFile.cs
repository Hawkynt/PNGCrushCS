using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MawWareTexture;

/// <summary>A Maw-Ware texture (.mtx): five little-endian words and then the pixels.</summary>
/// <remarks>
/// Nothing about this format has ever been published and no sample of it could be found. What
/// settles it instead is XnView's own converter, which reads the format and was made to say what it
/// expects: files were built to a hypothesis and handed to it, and the hypothesis was corrected
/// until every field it reports — the size, the number of components and the depth — came back as
/// the one written, and until the picture it converted out came back as the bytes that went in.
/// <para/>
/// The header is five 32-bit little-endian words: a constant 0x69, the width, the height, how many
/// bytes a pixel takes, and a word that is read and ignored. Changing the constant to 0x68 makes the
/// converter refuse the file; giving the last word any value at all changes nothing. The pixels
/// follow at offset 20, one row after another from the top, with no padding: one byte a pixel is a
/// grey, three are red, green and blue in that order, and four are those three with a fourth byte
/// behind them that the converter drops. All three were checked by converting to a portable pixmap
/// and comparing against what was written, and all three came back byte for byte.
/// <para/>
/// Two bytes a pixel are refused here. The converter accepts them and calls the result sixteen bits,
/// but what it converts out bears no relation to what went in under any of the obvious readings, so
/// there is nothing to implement against.
/// <para/>
/// The file also has to be exactly as long as its header says, which is stricter than XnView, which
/// draws a file whose pixels are cut short. A four-byte constant is a weak signature, so the length
/// is what really identifies one of these: a foreign file has to state a size that accounts for
/// itself to the byte before it is drawn. For the same reason the constant is not registered as a
/// signature — reading by bytes alone takes the first format whose signature matches and does not
/// try a second, and one byte with three zeros behind it is not enough to take a file away from
/// whatever else it might be.
/// </remarks>
public readonly record struct MawWareTextureFile
  : IImageFormatReader<MawWareTextureFile>, IImageToRawImage<MawWareTextureFile>,
    IImageFromRawImage<MawWareTextureFile>, IImageFormatWriter<MawWareTextureFile> {

  /// <summary>The word every one of these opens with.</summary>
  public const uint Magic = 0x69;

  /// <summary>Five words before the pixels.</summary>
  public const int HeaderSize = 20;

  static string IImageFormatMetadata<MawWareTextureFile>.PrimaryExtension => ".mtx";
  static string[] IImageFormatMetadata<MawWareTextureFile>.FileExtensions => [".mtx"];
  static MawWareTextureFile IImageFormatReader<MawWareTextureFile>.FromSpan(ReadOnlySpan<byte> data) => MawWareTextureReader.FromSpan(data);
  static byte[] IImageFormatWriter<MawWareTextureFile>.ToBytes(MawWareTextureFile file) => MawWareTextureWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MawWareTextureFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256, 16777216])
  ];

  /// <summary>How wide the texture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>How many bytes a pixel takes: 1, 3 or 4.</summary>
  public int BytesPerPixel { get; init; }

  /// <summary>The fifth word, which the format states and nothing reads.</summary>
  public uint Reserved { get; init; }

  /// <summary>The pixels as the file stores them.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MawWareTextureFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.BytesPerPixel switch {
        1 => PixelFormat.Gray8,
        3 => PixelFormat.Rgb24,
        _ => PixelFormat.Rgba32,
      },
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Builds the texture at whichever of the three widths the picture needs.</summary>
  public static MawWareTextureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var (bytes, format) = image.Format switch {
      PixelFormat.Gray8 => (1, PixelFormat.Gray8),
      PixelFormat.Rgb24 => (3, PixelFormat.Rgb24),
      _ => (4, PixelFormat.Rgba32),
    };

    var source = image.EnsureFormat(format);
    return new() {
      Width = source.Width,
      Height = source.Height,
      BytesPerPixel = bytes,
      Reserved = 0,
      PixelData = source.PixelData[..],
    };
  }
}
