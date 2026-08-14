using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>Reads HEIF/HEIC files from bytes, streams, or file paths.</summary>
public static class HeifReader {

  private const int _MIN_FILE_SIZE = 12; // at least ftyp box header + 4-byte brand

  private static readonly HashSet<string> _HEIF_BRANDS = new(StringComparer.Ordinal) {
    "heic", "heix", "hevc", "heim", "heis", "hevm", "hevs", "mif1",
  };

  public static HeifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HEIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HeifFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static HeifFile FromSpan(ReadOnlySpan<byte> data) {
    var container = _ReadContainer(data);
    var width = container.CodedWidth;
    var height = container.CodedHeight;
    var rawImageData = container.MdatPayload;

    // An hvcC property is the container stating that the item is HEVC-coded, which is what every
    // HEIF encoder writes; anything else here is our own writer's uncompressed payload. There is no
    // HEVC decoder in this project, so this is the point where that has to be said out loud rather
    // than answered with a raster.
    if (container.IsHevcCoded)
      throw new NotSupportedException(
        "HEIF: the picture is HEVC-coded (ISO/IEC 23008-2) and there is no HEVC decoder here. " +
        $"The container's extent is readable — {width}x{height} coded" +
        (container.Aperture != null ? ", with a clean aperture inside it" : string.Empty) +
        " — so HeifFile.ReadImageInfo still answers the size."
      );

    // What is left has to be the uncompressed payload HeifWriter emits for a container-level round
    // trip: exactly the picture's bytes. A payload of any other length is coded in some scheme that
    // has not been identified here, and padding the difference with zeroes would hand back a raster
    // nobody wrote. An extent that does not survive being turned into a byte count is left alone —
    // that is the malformed-container case the clean aperture guards already answer with an empty
    // raster rather than an exception.
    var expectedPixelBytes = width * height * 3;
    if (expectedPixelBytes > 0 && rawImageData.Length != expectedPixelBytes)
      throw new NotSupportedException(
        $"HEIF: the {width}x{height} item carries {rawImageData.Length} bytes where an uncompressed " +
        $"picture would be {expectedPixelBytes}, so it is coded in a scheme that is not implemented " +
        "here. HeifFile.ReadImageInfo still answers the size."
      );

    var pixelData = new byte[expectedPixelBytes > 0 ? expectedPixelBytes : 0];
    if (expectedPixelBytes > 0)
      rawImageData.AsSpan(0, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    // ispe carries the coded extent, which an encoder pads out to whole coding blocks; clap is what
    // states the picture inside it. Applied last so it trims the raster as well as the reported size.
    if (container.Aperture != null && _TryResolveCleanAperture(container.Aperture.Value, width, height, out var cropX, out var cropY, out var cropWidth, out var cropHeight)) {
      pixelData = _CropRgb24(pixelData, width, cropX, cropY, cropWidth, cropHeight);
      width = cropWidth;
      height = cropHeight;
    }

    return new HeifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Brand = container.Brand,
      RawImageData = rawImageData,
    };
  }

  /// <summary>The extent the container states, without touching the codestream.</summary>
  /// <remarks>
  /// ispe and clap are container properties, so the size of an HEVC-coded item is knowable even
  /// though its pixels are not. This is the path that keeps answering when <see cref="FromSpan"/>
  /// refuses.
  /// </remarks>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> data) {
    HeifContainer container;
    try {
      container = _ReadContainer(data);
    } catch (Exception) {
      // A metadata probe is handed arbitrary bytes and its contract is to answer "I do not know"
      // rather than throw: a box table can be malformed in ways the parser reports as any of half a
      // dozen exception types, and none of them is this method's caller's problem.
      return null;
    }

    var width = container.CodedWidth;
    var height = container.CodedHeight;
    if (width <= 0 || height <= 0)
      return null;

    if (container.Aperture != null && _TryResolveCleanAperture(container.Aperture.Value, width, height, out _, out _, out var cropWidth, out var cropHeight)) {
      width = cropWidth;
      height = cropHeight;
    }

    return new(width, height, 24, "Rgb24", container.IsHevcCoded ? "HEVC" : "None");
  }

  /// <summary>Everything the ISO base media boxes say, before any of it is turned into pixels.</summary>
  private readonly record struct HeifContainer(
    string Brand,
    int CodedWidth,
    int CodedHeight,
    CleanAperture? Aperture,
    bool IsHevcCoded,
    byte[] MdatPayload
  );

  private static HeifContainer _ReadContainer(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid HEIF file.");

    var bytes = data.ToArray();
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);

    var ftypBox = _FindBox(boxes, IsoBmffBox.Ftyp);
    if (ftypBox == null)
      throw new InvalidDataException("Missing ftyp box; not a valid ISOBMFF file.");

    var brand = _ReadBrand(ftypBox.Value.Data);
    if (!_HEIF_BRANDS.Contains(brand))
      throw new InvalidDataException($"Unsupported major brand '{brand}'; expected a HEIF brand.");

    var width = 0;
    var height = 0;
    byte[]? hvcCData = null;
    CleanAperture? clap = null;

    var metaBox = _FindBox(boxes, IsoBmffBox.Meta);
    if (metaBox != null)
      _ParseMetaBox(metaBox.Value.Data, ref width, ref height, ref hvcCData, ref clap);

    var mdatBox = _FindBox(boxes, IsoBmffBox.Mdat);
    var rawImageData = mdatBox?.Data ?? [];

    // hvcC is the container's own statement. A file that carries no properties at all but whose
    // mdat is a run of HEVC NAL units is still an HEVC item, so the payload is sniffed as well.
    var isHevcCoded = hvcCData != null
                      || (rawImageData.Length > 0 && rawImageData.Length != width * height * 3 && _LooksLikeHevcData(rawImageData));

    return new(brand, width, height, clap, isHevcCoded, rawImageData);
  }

  public static HeifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Checks whether data looks like HEVC NAL unit data (length-prefixed or Annex B).</summary>
  private static bool _LooksLikeHevcData(byte[] data) {
    if (data.Length < 4)
      return false;

    // Check for Annex B start code
    if (data[0] == 0 && data[1] == 0 && (data[2] == 1 || (data[2] == 0 && data.Length > 3 && data[3] == 1)))
      return true;

    // Check for length-prefixed NAL: first 4 bytes as BE length should be reasonable
    var length = BinaryPrimitives.ReadUInt32BigEndian(data);
    return length > 0 && length < (uint)data.Length;
  }

  private static string _ReadBrand(byte[] ftypData) {
    if (ftypData.Length < 4)
      return string.Empty;

    return Encoding.ASCII.GetString(ftypData.AsSpan(0, 4));
  }

  private static void _ParseMetaBox(byte[] data, ref int width, ref int height, ref byte[]? hvcCData, ref CleanAperture? clap) {
    // meta is a FullBox: skip version (1 byte) + flags (3 bytes)
    if (data.Length < 4)
      return;

    var subBoxes = IsoBmffBox.ReadBoxes(data, 4, data.Length - 4);
    foreach (var box in subBoxes) {
      if (box.Type == IsoBmffBox.Iprp)
        _ParseIprpBox(box.Data, ref width, ref height, ref hvcCData, ref clap);
    }
  }

  private static void _ParseIprpBox(byte[] data, ref int width, ref int height, ref byte[]? hvcCData, ref CleanAperture? clap) {
    var subBoxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);
    foreach (var box in subBoxes) {
      if (box.Type == IsoBmffBox.Ipco)
        _ParseIpcoBox(box.Data, ref width, ref height, ref hvcCData, ref clap);
    }
  }

  private static void _ParseIpcoBox(byte[] data, ref int width, ref int height, ref byte[]? hvcCData, ref CleanAperture? clap) {
    var subBoxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);
    foreach (var box in subBoxes) {
      if (box.Type == IsoBmffBox.Ispe && box.Data.Length >= 12) {
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(box.Data.AsSpan(4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(box.Data.AsSpan(8));
      } else if (box.Type == IsoBmffBox.Clap && box.Data.Length >= _CLAP_PAYLOAD_SIZE) {
        clap = _ReadCleanAperture(box.Data);
      } else if (box.Type == IsoBmffBox.HvcC) {
        hvcCData = box.Data;
      }
    }
  }

  // clap is a plain Box (no version/flags) holding eight 32-bit rationals: the aperture extent as
  // width and height fractions, then the offset of the aperture centre from the image centre.
  private const int _CLAP_PAYLOAD_SIZE = 32;

  /// <summary>The clean aperture as stored, before it is resolved against a raster.</summary>
  /// <remarks>
  /// ISO/IEC 14496-12 declares the extent numerators and every denominator unsigned and the two
  /// offset numerators signed, so the offsets are the only fields that may go negative. They
  /// routinely do: libheif writes -3/2 to place a 61-wide aperture at column 0 of a 64-wide raster.
  /// </remarks>
  private readonly record struct CleanAperture(
    uint WidthN, uint WidthD,
    uint HeightN, uint HeightD,
    int HorizOffN, uint HorizOffD,
    int VertOffN, uint VertOffD
  );

  private static CleanAperture _ReadCleanAperture(byte[] data) => new(
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)),
    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20)),
    BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(24)),
    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(28))
  );

  /// <summary>Turns a clean aperture into a pixel window inside a raster of the given extent.</summary>
  /// <returns><see langword="true"/> when the aperture is usable and actually crops something.</returns>
  /// <remarks>
  /// The spec puts the aperture centre at ((extent - 1) / 2 + offset) and its left edge half the
  /// aperture width further left, which reduces to left = (extent - cleanExtent) / 2 + offset. That
  /// form is used here because it stays exact in integers. Measured against the file libheif wrote:
  /// (64 - 61) / 2 + (-3/2) = 0 and (64 - 37) / 2 + (-27/2) = 0, matching the "left=0 top=0" that
  /// heif-info prints for it.
  /// <para/>
  /// Anything malformed — a zero denominator, an empty aperture, one larger than the raster — leaves
  /// the coded extent in place rather than throwing, since a reader that rejects the file outright
  /// is worse than one that reports the padded size the way it always did.
  /// <para/>
  /// An origin landing on a half pixel is floored, both axes alike. libheif floors the left edge but
  /// rounds the top edge up — a 1x1 aperture in a 64x64 raster comes back from heif-info as
  /// "left=31 top=32", both exactly 31.5 — which reads as an oversight rather than a rule, and the
  /// spec gives no rounding for a fractional aperture. The extent is what this reports, and that is
  /// the same either way; only the origin of a half-pixel crop differs.
  /// </remarks>
  private static bool _TryResolveCleanAperture(
    CleanAperture clap,
    int codedWidth,
    int codedHeight,
    out int cropX,
    out int cropY,
    out int cropWidth,
    out int cropHeight
  ) {
    cropX = cropY = cropWidth = cropHeight = 0;

    if (clap.WidthD == 0 || clap.HeightD == 0 || clap.HorizOffD == 0 || clap.VertOffD == 0)
      return false;

    if (codedWidth <= 0 || codedHeight <= 0)
      return false;

    // Extents are rounded to the nearest pixel; a fractional aperture cannot be handed back as a
    // raster. (2N + D) / 2D is round-half-up, and both fields are unsigned, so this stays exact.
    var cleanWidth = (2L * clap.WidthN + clap.WidthD) / (2L * clap.WidthD);
    var cleanHeight = (2L * clap.HeightN + clap.HeightD) / (2L * clap.HeightD);

    if (cleanWidth <= 0 || cleanHeight <= 0 || cleanWidth > codedWidth || cleanHeight > codedHeight)
      return false;

    // The coded extent above can itself be nonsense: width * height * 3 is computed in int further
    // up and a crafted ispe overflows it to a negative, which allocates nothing and leaves the huge
    // extent in place. Refuse an aperture whose raster could not be indexed rather than overflow a
    // second allocation here.
    if ((long)cleanWidth * cleanHeight * 3 > int.MaxValue)
      return false;

    if (cleanWidth == codedWidth && cleanHeight == codedHeight)
      return false; // nothing to crop; skip the copy

    // left = (codedWidth - cleanWidth) / 2 + HorizOffN / HorizOffD, over the common denominator 2*D.
    var left = _FloorDiv((codedWidth - cleanWidth) * clap.HorizOffD + 2L * clap.HorizOffN, 2L * clap.HorizOffD);
    var top = _FloorDiv((codedHeight - cleanHeight) * clap.VertOffD + 2L * clap.VertOffN, 2L * clap.VertOffD);

    // An aperture that hangs off the edge is clamped back inside rather than discarded: the extent
    // it states is still the picture's, only its stated position is unusable.
    cropX = (int)Math.Clamp(left, 0, codedWidth - cleanWidth);
    cropY = (int)Math.Clamp(top, 0, codedHeight - cleanHeight);
    cropWidth = (int)cleanWidth;
    cropHeight = (int)cleanHeight;
    return true;
  }

  /// <summary>Integer division rounding toward negative infinity; <paramref name="divisor"/> is positive.</summary>
  private static long _FloorDiv(long dividend, long divisor) {
    var quotient = Math.DivRem(dividend, divisor, out var remainder);
    return remainder < 0 ? quotient - 1 : quotient;
  }

  /// <summary>Copies a window out of an Rgb24 raster.</summary>
  private static byte[] _CropRgb24(byte[] source, int sourceWidth, int x, int y, int width, int height) {
    const int BYTES_PER_PIXEL = 3;
    var result = new byte[width * height * BYTES_PER_PIXEL];
    var rowBytes = width * BYTES_PER_PIXEL;

    for (var row = 0; row < height; ++row) {
      // 64-bit: the coded width is whatever ispe claimed, so a row offset can leave int range even
      // when the window itself is tiny.
      var sourceOffset = ((long)(y + row) * sourceWidth + x) * BYTES_PER_PIXEL;
      if (sourceOffset < 0 || sourceOffset + rowBytes > source.Length)
        break; // short raster (a failed decode leaves one); the rest stays zeroed

      source.AsSpan((int)sourceOffset, rowBytes).CopyTo(result.AsSpan(row * rowBytes));
    }

    return result;
  }

  private static IsoBmffBox? _FindBox(List<IsoBmffBox> boxes, string type) {
    foreach (var box in boxes)
      if (box.Type == type)
        return box;

    return null;
  }
}
