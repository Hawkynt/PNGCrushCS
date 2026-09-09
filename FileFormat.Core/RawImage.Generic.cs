using System;

namespace FileFormat.Core;

/// <summary>
/// Compile-time typed view of a <see cref="RawImage"/>. The type parameter declares the semantic raw
/// pixel representation while the wrapped legacy image keeps current readers, converters, and writers
/// working during migration.
/// </summary>
/// <typeparam name="TPixel">Raw pixel-format descriptor.</typeparam>
public sealed class RawImage<TPixel> where TPixel : IRawPixelFormat<TPixel> {
  private readonly RawImage _image;

  /// <summary>Creates a typed raw image using the compatibility storage declared by <typeparamref name="TPixel"/>.</summary>
  public RawImage(
    int width,
    int height,
    byte[] pixelData,
    RawImageColorInfo? colorInfo = null,
    byte[]? palette = null,
    int paletteCount = 0,
    byte[]? alphaTable = null,
    ImageMetadata? metadata = null
  ) {
    ArgumentNullException.ThrowIfNull(pixelData);

    this._image = new() {
      Width = width,
      Height = height,
      Format = TPixel.Traits.LegacyFormat,
      PixelData = pixelData,
      ColorInfo = colorInfo,
      Palette = palette,
      PaletteCount = paletteCount,
      AlphaTable = alphaTable,
      Metadata = metadata,
    };

    this.Validate();
  }

  private RawImage(RawImage image) {
    this._image = image;
    this.Validate();
  }

  /// <summary>Static traits of the representation carried in the generic type.</summary>
  public static RawPixelFormatTraits PixelTraits => TPixel.Traits;

  /// <summary>Compatibility runtime format used by the current non-generic pipeline.</summary>
  public PixelFormat Format => TPixel.Traits.LegacyFormat;

  public int Width => this._image.Width;
  public int Height => this._image.Height;
  public byte[] PixelData => this._image.PixelData;
  public RawImageColorInfo? ColorInfo => this._image.ColorInfo;
  public byte[]? Palette => this._image.Palette;
  public int PaletteCount => this._image.PaletteCount;
  public byte[]? AlphaTable => this._image.AlphaTable;
  public ImageMetadata? Metadata => this._image.Metadata;
  public bool IsIndexed => TPixel.Traits.IsIndexed;
  public bool IsPlanarYuv => TPixel.Traits.IsPlanarYuv;
  public bool IsFloatingPoint => TPixel.Traits.IsFloatingPoint;
  public bool HasAlpha => this._image.HasAlpha;
  public int PlaneCount => this._image.PlaneCount;
  public long MinimumPixelDataLength => this._image.MinimumPixelDataLength;
  public bool HasEnoughPixelData => this._image.HasEnoughPixelData;

  /// <summary>
  /// Legacy view of this image. Conversion is zero-copy: the same pixel/palette/metadata arrays are used.
  /// This property is transitional and lets typed adoption proceed before every current API is generic.
  /// </summary>
  public RawImage Untyped => this._image;

  public (int Width, int Height) GetPlaneDimensions(int plane) => this._image.GetPlaneDimensions(plane);
  public int GetPlaneOffset(int plane) => this._image.GetPlaneOffset(plane);
  public int GetPlaneLength(int plane) => this._image.GetPlaneLength(plane);
  public ReadOnlySpan<byte> GetPlaneData(int plane) => this._image.GetPlaneData(plane);

  /// <summary>Validates value-level invariants not expressible solely by the generic type.</summary>
  public void Validate() => RawPixelFormats.ValidateDeclaredRepresentation(this._image, TPixel.Traits);

  /// <summary>
  /// Adds a typed view to an existing legacy image without copying its buffers. Narrow logical indexed
  /// types such as <c>Indexed6</c> are checked so an Indexed8 buffer containing index 200 cannot be
  /// mislabeled as six-bit data.
  /// </summary>
  public static RawImage<TPixel> FromUntyped(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new(image);
  }

  public static bool TryFromUntyped(RawImage image, out RawImage<TPixel>? result) {
    ArgumentNullException.ThrowIfNull(image);

    try {
      result = new(image);
      return true;
    } catch (ArgumentException) {
      result = null;
      return false;
    } catch (OverflowException) {
      result = null;
      return false;
    }
  }

  /// <summary>Implicitly enters the legacy runtime-tagged pipeline without copying pixel data.</summary>
  public static implicit operator RawImage(RawImage<TPixel> image) {
    ArgumentNullException.ThrowIfNull(image);
    return image._image;
  }
}
