using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.KodakDc25;

/// <summary>In-memory representation of a Kodak DC25 raw photograph (.k25).</summary>
/// <remarks>
/// The file is a big-endian TIFF-shaped camera container followed by an eight-bit four-colour
/// complementary sensor array at the camera's fixed offset. Reading demosaics and restores the
/// non-square photosite aspect. Writing uses the wide 512x244 sensor mode and a minimum-norm inverse
/// of that same colour transform, so arbitrary RGB can be encoded as a legal camera RAW rather than
/// merely wrapped as a thumbnail.
/// </remarks>
[FormatDetectionPriority(10)]
public readonly record struct KodakDc25File :
  IImageFormatReader<KodakDc25File>, IImageToRawImage<KodakDc25File>,
  IImageFromRawImage<KodakDc25File>, IImageFormatWriter<KodakDc25File> {

  public const string Model = "KODAK DC25";
  public const int SensorOffset = 15424;
  public const int SensorHeight = 244;
  public const int WideSensorWidth = 512;
  public const int NarrowSensorWidth = 256;
  public const int WideWidth = 501;
  public const int NarrowWidth = 249;
  public const int CroppedHeight = 242;
  public const int RenderedWidth = 493;
  public const int RenderedHeight = 373;
  public const int WideOutputWidth = 501;
  public const int WideOutputHeight = 379;
  public const int NarrowOutputWidth = 323;
  public const int NarrowOutputHeight = 242;

  static string IImageFormatMetadata<KodakDc25File>.PrimaryExtension => ".k25";
  static string[] IImageFormatMetadata<KodakDc25File>.FileExtensions => [".k25"];
  static KodakDc25File IImageFormatReader<KodakDc25File>.FromSpan(ReadOnlySpan<byte> data) => KodakDc25Reader.FromSpan(data);
  static byte[] IImageFormatWriter<KodakDc25File>.ToBytes(KodakDc25File file) => KodakDc25Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<KodakDc25File>.VideoModes => [
    new("Wide sensor", [(WideOutputWidth, WideOutputHeight)], [16777216]),
    new("Narrow sensor", [(NarrowOutputWidth, NarrowOutputHeight)], [16777216])
  ];

  static bool? IImageFormatMetadata<KodakDc25File>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < SensorOffset || header[0] != 'M' || header[1] != 'M' || header[2] != 0x00 || header[3] != 0x2A)
      return null;

    if (header.Length != SensorOffset + WideSensorWidth * SensorHeight
        && header.Length != SensorOffset + NarrowSensorWidth * SensorHeight)
      return null;

    return Encoding.ASCII.GetString(header[..SensorOffset]).Contains(Model, StringComparison.Ordinal) ? true : null;
  }

  /// <summary>Decoded image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Decoded image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Decoded RGB24 pixels when this object came from a reader or source image.</summary>
  public byte[]? PixelData { get; init; }

  /// <summary>Raw 8-bit four-colour photosites to serialize, when available.</summary>
  public byte[]? SensorData { get; init; }

  /// <summary>Whether <see cref="SensorData"/> is the 512-wide rather than 256-wide camera mode.</summary>
  public bool IsWideSensor { get; init; }

  public static RawImage ToRawImage(KodakDc25File file) {
    if (file.PixelData is { Length: > 0 })
      return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = file.PixelData[..] };

    if (file.SensorData is { Length: > 0 })
      return KodakDc25File.ToRawImage(KodakDc25Reader.FromBytes(KodakDc25Writer.ToBytes(file)));

    throw new InvalidOperationException("A Kodak DC25 file carries neither decoded pixels nor sensor data.");
  }

  public static KodakDc25File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var source = image.SampleTo(WideOutputWidth, WideOutputHeight);
    return new() {
      Width = source.Width,
      Height = source.Height,
      PixelData = source.PixelData,
      SensorData = KodakDc25Inverse.FromRgb(source),
      IsWideSensor = true,
    };
  }
}
