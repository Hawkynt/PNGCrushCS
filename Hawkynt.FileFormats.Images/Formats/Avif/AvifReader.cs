using System;
using System.IO;
using System.Linq;
using FileFormat.Avif.Codec;

namespace FileFormat.Avif;

/// <summary>Reads AVIF files from bytes, streams, or file paths.</summary>
/// <remarks>
/// The picture inside an AVIF file is an AV1 key frame, so this reads the ISO base media container
/// down to the primary item's bytes and then hands them to the AV1 decoder under <c>Codec</c>. That
/// decoder covers what AVIF still pictures actually contain — intra frames, 8-bit 4:2:0 and 4:4:4,
/// monochrome — and refuses anything outside it by name rather than guessing.
/// </remarks>
public static class AvifReader {

  private const int _MIN_FILE_SIZE = 12;
  private const string _AVIF_BRAND = "avif";
  private const string _AVIS_BRAND = "avis";

  public static AvifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AvifFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AvifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AvifFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid AVIF file.");

    var bytes = data.ToArray();
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);
    var ftyp = boxes.FirstOrDefault(b => b.Type == IsoBmffBox.Ftyp)
      ?? throw new InvalidDataException("Missing ftyp box.");

    var brand = _ReadBrand(ftyp.Data);
    if (brand != _AVIF_BRAND && brand != _AVIS_BRAND)
      throw new InvalidDataException($"Invalid AVIF brand: '{brand}'.");

    var layout = AvifItemLayout.Parse(bytes);
    var primary = layout.PrimaryItemId;
    if (layout.ItemTypes.TryGetValue(primary, out var itemType) && itemType != "av01")
      throw new NotSupportedException(
        $"AVIF: the primary item is of type '{itemType}'. Only AV1-coded items ('av01') are supported.");

    var coded = _GatherCodedData(bytes, layout, primary);
    var (width, height, rgb) = Av1FrameDecoder.Decode(coded, 0, coded.Length);

    var alphaItem = layout.FindAlphaItem(primary);
    byte[]? alpha = null;
    if (alphaItem is { } alphaId) {
      var alphaCoded = _GatherCodedData(bytes, layout, alphaId);
      alpha = _DecodeAlphaPlane(alphaCoded, width, height);
    }

    var pixelData = alpha == null ? rgb : _Interleave(rgb, alpha, width, height);

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
      HasAlpha = alpha != null,
      Brand = brand,
      RawImageData = coded,
    };
  }

  /// <summary>
  /// Concatenates the configuration OBUs from <c>av1C</c> with the item's own bytes. Most encoders
  /// leave <c>configOBUs</c> empty and put the sequence header in the item, but the box is allowed
  /// to carry it, and a decoder that ignores it sees a frame with no sequence header.
  /// </summary>
  private static byte[] _GatherCodedData(byte[] file, AvifItemLayout layout, uint itemId) {
    var payload = layout.GetItemData(file, itemId);
    var av1C = layout.GetProperty(itemId, IsoBmffBox.Av1C);
    if (av1C is not { Length: > 4 })
      return payload;

    var configObus = av1C.AsSpan(4);
    if (configObus.IsEmpty)
      return payload;

    var result = new byte[configObus.Length + payload.Length];
    configObus.CopyTo(result);
    payload.CopyTo(result.AsSpan(configObus.Length));
    return result;
  }

  private static byte[] _DecodeAlphaPlane(byte[] coded, int width, int height) {
    var result = Av1FrameDecoder.DecodeToPlanes(coded, 0, coded.Length);
    if (!result.Sequence.MonoChrome)
      throw new NotSupportedException("AVIF: the alpha auxiliary item is not monochrome.");

    var plane = result.Picture.Planes[0];
    var stride = result.Picture.Strides[0];
    var shift = result.Sequence.BitDepth - 8;
    var alpha = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var sample = y < result.Frame.FrameHeight && x < result.Frame.FrameWidth ? plane[y * stride + x] : 255 << shift;
        alpha[y * width + x] = (byte)Math.Clamp(sample >> shift, 0, 255);
      }

    return alpha;
  }

  private static byte[] _Interleave(byte[] rgb, byte[] alpha, int width, int height) {
    var result = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      result[i * 4] = rgb[i * 3];
      result[i * 4 + 1] = rgb[i * 3 + 1];
      result[i * 4 + 2] = rgb[i * 3 + 2];
      result[i * 4 + 3] = alpha[i];
    }
    return result;
  }

  private static string _ReadBrand(byte[] ftypData) {
    if (ftypData.Length < 4)
      throw new InvalidDataException("Invalid ftyp box data.");

    return new([(char)ftypData[0], (char)ftypData[1], (char)ftypData[2], (char)ftypData[3]]);
  }
}
