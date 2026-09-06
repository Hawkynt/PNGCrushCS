using System;
using FileFormat.Core;

namespace FileFormat.IffDctv;

/// <summary>In-memory representation of an IFF DCTV (Digital Composite Television) image.</summary>
/// <remarks>
/// The payload is one 4-bit composite sample nibble per pixel, signature lines included, which is
/// what the file actually stores — the ILBM around it is a carrier and its colour map is a set of
/// DAC levels rather than picture colours. See <see cref="IffDctvCodec"/> for the encoding.
/// </remarks>
public readonly record struct IffDctvFile : IImageFormatReader<IffDctvFile>, IImageToRawImage<IffDctvFile>, IImageFromRawImage<IffDctvFile>, IImageFormatWriter<IffDctvFile> {

  /// <summary>Minimum valid file size (FORM header plus a BMHD chunk).</summary>
  internal const int MinFileSize = 32;

  /// <summary>Tallest picture this package will write.</summary>
  internal const int MaximumHeight = 1024;

  static string IImageFormatMetadata<IffDctvFile>.PrimaryExtension => ".dctv";
  // Genuine DCTV files also turn up as .dct and .iff, but both already belong to the plain ILBM
  // entry here, and a DCTV picture is an ILBM — claiming them would only make detection guess.
  static string[] IImageFormatMetadata<IffDctvFile>.FileExtensions => [".dctv"];
  static IffDctvFile IImageFormatReader<IffDctvFile>.FromSpan(ReadOnlySpan<byte> data) => IffDctvReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffDctvFile>.ToBytes(IffDctvFile file) => IffDctvWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<IffDctvFile>.VideoModes => [
    new("DCTV composite", [(new IntegerRange(IffDctvCodec.MinimumWidth, 1024, 2), new IntegerRange(1, MaximumHeight))])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Number of stored rows, including the one or two signature lines at the top.</summary>
  public int ContentHeight { get; init; }

  /// <summary>Whether the screen is interlaced, which is also what makes the top two rows signature
  /// lines instead of one and gives the picture square rather than double-height pixels.</summary>
  public bool Interlaced { get; init; }

  /// <summary>Bitplanes the carrier ILBM used. Four is full precision; three drops each sample's low bit.</summary>
  public int PlaneCount { get; init; }

  /// <summary>One 4-bit composite sample nibble per pixel, <see cref="Width"/> × <see cref="ContentHeight"/>.</summary>
  public byte[] Samples { get; init; }

  /// <summary>Displayed picture height, once the signature lines and the row doubling are accounted for.</summary>
  public int Height => (this.ContentHeight - (this.Interlaced ? 2 : 1)) * (this.Interlaced ? 1 : 2);

  /// <summary>Reconstructs the picture the DCTV unit would have shown.</summary>
  public static RawImage ToRawImage(IffDctvFile file) {
    Validate(file, nameof(file));

    var width = file.Width;
    var lines = file.ContentHeight - (file.Interlaced ? 2 : 1);
    var rgb = IffDctvCodec.Decode(file.Samples, width, file.ContentHeight, file.Interlaced);
    if (file.Interlaced)
      return new() { Width = width, Height = lines, Format = PixelFormat.Rgb24, PixelData = rgb };

    // A non-interlaced DCTV screen is a half-height picture shown on a full-height display, so every
    // stored row covers two scanlines.
    var doubled = new byte[width * lines * 2 * 3];
    var stride = width * 3;
    for (var y = 0; y < lines; ++y) {
      var source = rgb.AsSpan(y * stride, stride);
      source.CopyTo(doubled.AsSpan(y * 2 * stride, stride));
      source.CopyTo(doubled.AsSpan((y * 2 + 1) * stride, stride));
    }

    return new() { Width = width, Height = lines * 2, Format = PixelFormat.Rgb24, PixelData = doubled };
  }

  /// <summary>
  /// Encodes a picture as a DCTV composite screen.
  /// </summary>
  /// <remarks>
  /// This is lossy by construction and cannot be otherwise: luminance is recovered as the mean of two
  /// neighbouring samples and chrominance is carried at half the horizontal and half the vertical
  /// rate, so a DCTV file holds an approximation of the picture and never the picture. What is
  /// refused here is only what the format cannot carry at all — a picture too narrow for the
  /// signature line, one wider than the reconstruction's delay line, or an odd width, which would
  /// leave a sample straddling the edge with nothing to pair against.
  /// </remarks>
  public static IffDctvFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width < IffDctvCodec.MinimumWidth)
      throw new ArgumentException(
        $"IFF DCTV pictures must be at least {IffDctvCodec.MinimumWidth} pixels wide: the synchronisation sequence the decoder looks for occupies {IffDctvCodec.SignatureLength} pixels at each end of the top line and does not fit in a narrower screen.",
        nameof(image));

    if (image.Width > IffDctvCodec.MaximumWidth)
      throw new ArgumentException(
        $"IFF DCTV pictures must be at most {IffDctvCodec.MaximumWidth} pixels wide.", nameof(image));

    if ((image.Width & 1) != 0)
      throw new ArgumentException(
        "IFF DCTV pictures must have an even width: one composite sample is split across two adjacent pixels, so an odd width would leave a sample with no partner at the right edge.",
        nameof(image));

    if (image.Height is < 1 or > MaximumHeight)
      throw new ArgumentException(
        $"IFF DCTV pictures must be between 1 and {MaximumHeight} pixels tall.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      ContentHeight = image.Height + 2,
      Interlaced = true,
      PlaneCount = IffDctvCodec.WrittenPlaneCount,
      Samples = IffDctvCodec.Encode(rgb.PixelData, image.Width, image.Height),
    };
  }

  internal static void Validate(IffDctvFile file, string parameterName) {
    if (file.Width < IffDctvCodec.MinimumWidth || file.Width > IffDctvCodec.MaximumWidth)
      throw new ArgumentException($"IFF DCTV width {file.Width} is outside the range the format allows.", parameterName);

    var minimumRows = file.Interlaced ? 3 : 2;
    if (file.ContentHeight < minimumRows)
      throw new ArgumentException($"An IFF DCTV screen needs at least {minimumRows} rows.", parameterName);

    if (file.Samples is null || file.Samples.Length != file.Width * file.ContentHeight)
      throw new ArgumentException(
        $"IFF DCTV sample data must hold exactly {file.Width * file.ContentHeight} nibbles.", parameterName);
  }
}
