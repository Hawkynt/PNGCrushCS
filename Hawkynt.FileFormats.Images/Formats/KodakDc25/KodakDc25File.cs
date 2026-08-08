using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.KodakDc25;

/// <summary>In-memory representation of a Kodak DC25 raw photograph (.k25).</summary>
/// <remarks>
/// The file is a valid big-endian TIFF, and that is the trouble with it. Its first directory is an
/// eighty by sixty thumbnail marked as a reduced-resolution copy, and every TIFF reader shows that
/// and stops. The photograph is in a sub-directory, and the sub-directory's own tags are wrong: it
/// states 493 by 373 with a strip of 124928 bytes, and 493 by 373 is 183889.
/// <para/>
/// What accounts exactly is the sensor rather than the picture. 124928 is 512 by 244, and 15424 plus
/// 124928 is the file's length to the byte; the smaller files are 256 by 244 and account the same
/// way. So the stored array is 512 or 256 photosites across and 244 down, one byte each, and the
/// stated 493 by 373 is the size Kodak's own software rendered to after demosaicing and stretching
/// for the camera's non-square pixels. The offset is fixed at 15424 and the directory's own strip
/// offset is not used, which is what dcraw does too.
/// <para/>
/// The mosaic is not Bayer. It is a four-colour complementary array — magenta and yellow on one row,
/// green and cyan on the next — so the three primaries are recovered by a matrix over four channels
/// rather than by picking one of three.
/// <para/>
/// This claims the file ahead of the TIFF reader because both are right about what the bytes are and
/// only one of them is right about which picture was wanted.
/// </remarks>
[FormatDetectionPriority(10)]
public readonly record struct KodakDc25File : IImageFormatReader<KodakDc25File>, IImageToRawImage<KodakDc25File> {

  /// <summary>The camera's own name, which the TIFF carries as its model.</summary>
  public const string Model = "KODAK DC25";

  /// <summary>Where the sensor array begins, whatever the directory says.</summary>
  public const int SensorOffset = 15424;

  /// <summary>How many rows the array has, including the one that is cropped away.</summary>
  public const int SensorHeight = 244;

  /// <summary>The wider of the two arrays these cameras wrote.</summary>
  public const int WideSensorWidth = 512;

  /// <summary>The narrower one.</summary>
  public const int NarrowSensorWidth = 256;

  /// <summary>What the wider array renders to once its margins are dropped.</summary>
  public const int WideWidth = 501;

  /// <summary>And the narrower one.</summary>
  public const int NarrowWidth = 249;

  /// <summary>Both lose one row at the top and one at the bottom.</summary>
  public const int CroppedHeight = 242;

  /// <summary>The width the camera says it rendered the wider array to.</summary>
  public const int RenderedWidth = 493;

  /// <summary>And the height, which is what makes the photosites non-square.</summary>
  public const int RenderedHeight = 373;

  static string IImageFormatMetadata<KodakDc25File>.PrimaryExtension => ".k25";
  static string[] IImageFormatMetadata<KodakDc25File>.FileExtensions => [".k25"];
  static KodakDc25File IImageFormatReader<KodakDc25File>.FromSpan(ReadOnlySpan<byte> data) => KodakDc25Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<KodakDc25File>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// A big-endian TIFF naming this camera whose length is exactly the sensor array laid after the
  /// fixed offset. All three have to hold: the name alone would take a file of some other Kodak
  /// vintage, and the arithmetic alone would take any TIFF that happened to be the right length.
  /// </summary>
  static bool? IImageFormatMetadata<KodakDc25File>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < SensorOffset || header[0] != 'M' || header[1] != 'M' || header[2] != 0x00 || header[3] != 0x2A)
      return null;

    if (header.Length != SensorOffset + WideSensorWidth * SensorHeight
        && header.Length != SensorOffset + NarrowSensorWidth * SensorHeight)
      return null;

    return Encoding.ASCII.GetString(header[..SensorOffset]).Contains(Model, StringComparison.Ordinal) ? true : null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in RGB24 interleaved order.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(KodakDc25File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }
}
