using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace FileFormat.Fif;

/// <summary>Reads Iterated Systems fractal images: FTC 1.1, FTC 3.1–3.3 and progressive FIF1.</summary>
/// <remarks>
/// Decoding follows Iterated's own DECO_32.DLL 6.1 with its default output settings: the result is
/// the picture that DLL draws at the image's original size, deblocking included, or at any other size
/// through <see cref="FromFile(FileInfo, int, int)"/> and <see cref="FromBytes(byte[], byte[], int, int)"/>. An FTC 3.x file may
/// draw on an external FTT template (<c>*.FTT</c>); <see cref="FromFile(FileInfo)"/> finds it beside
/// the file by the name the file records, and <see cref="FromBytes(byte[], byte[])"/> takes it
/// directly. The FTC video profiles are refused by name.
/// </remarks>
public static class FifReader {

  /// <summary>Reads a fractal image, looking for any template it needs beside it.</summary>
  public static FifFile FromFile(FileInfo file) {
    var (data, template) = _Load(file);
    return _Read(data, template);
  }

  /// <summary>
  /// Reads a fractal image drawn at <paramref name="width"/>×<paramref name="height"/>, looking for any
  /// template it needs beside it.
  /// </summary>
  /// <remarks>
  /// A fractal picture has no fixed pixel grid. Iterated's decoder draws the transforms at a
  /// power-of-two multiple of the coded size (half resolution at the least) and area-averages that
  /// down to the size asked for; this reader does the same, so the result is what DECO_32.DLL draws
  /// after <c>SetOutputResolution(width, height)</c>.
  /// </remarks>
  public static FifFile FromFile(FileInfo file, int width, int height) {
    var (data, template) = _Load(file);
    return _Read(data, template, _Size(width, height));
  }

  private static (byte[] Data, byte[]? Template) _Load(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fractal image file not found.", file.FullName);

    var data = File.ReadAllBytes(file.FullName);
    var reference = TemplateName(data);
    byte[]? template = null;
    if (reference != null && file.Directory is { Exists: true } directory) {
      var match = directory.EnumerateFiles()
        .FirstOrDefault(f => string.Equals(f.Name, reference, StringComparison.OrdinalIgnoreCase));
      if (match != null)
        template = File.ReadAllBytes(match.FullName);
    }

    return (data, template);
  }

  private static (int, int) _Size(int width, int height) {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
    return (width, height);
  }

  public static FifFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static FifFile FromSpan(ReadOnlySpan<byte> data) => _Read(data.ToArray(), null);

  public static FifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Read(data, null);
  }

  /// <summary>Reads a fractal image that draws on the given FTT template.</summary>
  public static FifFile FromBytes(byte[] data, byte[]? template) {
    ArgumentNullException.ThrowIfNull(data);
    return _Read(data, template);
  }

  /// <summary>Reads a fractal image drawn at <paramref name="width"/>×<paramref name="height"/>, on the given FTT template if it needs one.</summary>
  /// <remarks>See <see cref="FromFile(FileInfo, int, int)"/> for how other sizes are drawn.</remarks>
  public static FifFile FromBytes(byte[] data, byte[]? template, int width, int height) {
    ArgumentNullException.ThrowIfNull(data);
    return _Read(data, template, _Size(width, height));
  }

  /// <summary>The coded size of a fractal still image: the size it is drawn at unless another is asked for.</summary>
  public static (int Width, int Height) CodedSize(ReadOnlySpan<byte> data) {
    var bytes = data.ToArray();
    if (FifProgressive.Matches(bytes)) {
      var fif = FifProgressive.Parse(bytes);
      return (fif.OutputWidth, fif.OutputHeight);
    }

    if (FifRegionProfile.Matches(bytes)) {
      var profile = FifRegionProfile.Parse(bytes);
      return (profile.OutputWidth, profile.OutputHeight);
    }

    if (FifFixedProfile.Matches(bytes))
      return FifFixedProfile.Size(bytes);

    throw new InvalidDataException("Not an Iterated Systems fractal still image (FTC 1.1, FTC 3.x or FIF1).");
  }

  /// <summary>The file name (without directory) of the FTT template a picture needs, or <c>null</c>.</summary>
  public static string? TemplateName(ReadOnlySpan<byte> data) {
    if (!FifRegionProfile.Matches(data))
      return null;

    try {
      var reference = FifRegionProfile.Parse(data.ToArray()).Templates.FirstOrDefault();
      if (reference == null)
        return null;

      var name = reference.FileName;
      var slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
      return slash >= 0 ? name[(slash + 1)..] : name;
    } catch (InvalidDataException) {
      return null;
    }
  }

  private static readonly byte[] _videoGray = "FTC\0\u0001\u0001\u0001\u0002"u8.ToArray();
  private static readonly byte[] _videoColor = "FTC\0\u0001\u0001\u0002\u0002"u8.ToArray();
  private static readonly byte[] _videoCompact = "FTC\0\u0002\u0002\u0002\u0002"u8.ToArray();

  private static FifFile _Read(byte[] data, byte[]? templateData) => _Read(data, templateData, null);

  private static FifFile _Read(byte[] data, byte[]? templateData, (int Width, int Height)? size) {
    if (data.Length < 8)
      throw new InvalidDataException("Data too small for a fractal image.");

    var head = data.AsSpan(0, 8);
    if (head.SequenceEqual(_videoGray) || head.SequenceEqual(_videoColor) || head.SequenceEqual(_videoCompact))
      throw new NotSupportedException("This FTC file is P.OEM / FJF fractal video, not a still image.");

    if (FifProgressive.Matches(data)) {
      var fif = FifProgressive.Parse(data);
      var (w, h, rgb) = fif.Decode(deblock: true, size);
      return new() { Width = w, Height = h, Variant = FifVariant.Progressive, PixelData = rgb };
    }

    if (FifRegionProfile.Matches(data)) {
      var profile = FifRegionProfile.Parse(data);
      FifTemplateRaster? template = null;
      if (profile.Templates.Count > 0) {
        var reference = profile.Templates[0];
        if (templateData == null)
          throw new InvalidDataException($"This FTC picture draws on the FTT template '{reference.FileName}', which was not supplied.");

        template = ParseTemplate(templateData, reference);
      }

      var plan = FifOutputPlan.For(profile.OutputWidth, profile.OutputHeight, size?.Width ?? profile.OutputWidth, size?.Height ?? profile.OutputHeight);
      var (w, h, rgb) = profile.Decode(template, deblock: true, plan);
      return new() { Width = w, Height = h, Variant = FifVariant.Region, PixelData = rgb };
    }

    if (FifFixedProfile.Matches(data)) {
      var (w, h, rgb) = FifFixedProfile.Decode(data, deblock: true, size);
      return new() { Width = w, Height = h, Variant = FifVariant.Fixed, PixelData = rgb };
    }

    if (data.AsSpan(0, 4).SequenceEqual("FTC\0"u8) && data.Length >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)) == 112)
      throw new NotSupportedException("This is an early FTC picture built on an external transform raster, which is not supported.");

    throw new InvalidDataException("Not an Iterated Systems fractal image (FTC 1.1, FTC 3.x or FIF1).");
  }

  /// <summary>Parses an FTT 1.1 template and checks it is the one the picture names.</summary>
  internal static FifTemplateRaster ParseTemplate(byte[] data, FifTemplateReference reference) {
    if (data.Length < 26 || !data.AsSpan(0, 4).SequenceEqual("FTT\0"u8))
      throw new InvalidDataException("The template is not an FTT file.");
    if (data[4] != 0 || data[5] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)) != 0x0101)
      throw new InvalidDataException("Unsupported FTT header version.");

    int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
    int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
    if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)) != 8 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)) != 0
        || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16)) != 26)
      throw new InvalidDataException("Unsupported FTT raster geometry.");

    var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(18));
    var identifier = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(22));
    if (data.Length != 26 + length || length != (uint)(width * height))
      throw new InvalidDataException("The FTT raster length is inconsistent.");
    if (width != reference.Width || height != reference.Height || identifier != reference.Identifier)
      throw new InvalidDataException($"The FTT template is not '{reference.FileName}' ({reference.Width}x{reference.Height}, 0x{reference.Identifier:x8}).");

    return new(width, height, identifier, data[26..]);
  }
}
