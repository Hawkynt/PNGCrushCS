using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Eri;

/// <summary>Reads Entis Rasterized Image (.eri) pictures and Moving Entis Image (.mei) animations.</summary>
public static class EriReader {

  /// <summary>"Entis", 1AH, 0, 0: the EMC container's signature.</summary>
  internal static ReadOnlySpan<byte> Signature => [0x45, 0x6E, 0x74, 0x69, 0x73, 0x1A, 0x00, 0x00];

  /// <summary>The file identifier of every ERI-family file.</summary>
  internal const uint RasterizedImageId = 0x03000100;

  /// <summary>Where the first record starts, after the signature, identifier and format name.</summary>
  internal const int RecordsOffset = 0x40;

  /// <summary>The largest picture side accepted, which keeps a damaged header from asking for gigabytes.</summary>
  internal const int MaxDimension = 1 << 15;

  public static EriFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ERI file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EriFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static EriFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  internal readonly record struct Record(ulong Id, int Offset, int Length);

  internal static ulong Id(string name) => BinaryPrimitives.ReadUInt64LittleEndian(Encoding.ASCII.GetBytes(name));

  internal static readonly ulong HeaderId = Id("Header  ");
  internal static readonly ulong StreamId = Id("Stream  ");
  internal static readonly ulong FileHeaderId = Id("FileHdr ");
  internal static readonly ulong ImageInfoId = Id("ImageInf");
  internal static readonly ulong PreviewInfoId = Id("PrevwInf");
  internal static readonly ulong SoundInfoId = Id("SoundInf");
  internal static readonly ulong SequenceId = Id("Sequence");
  internal static readonly ulong CopyrightId = Id("cpyright");
  internal static readonly ulong DescriptionId = Id("descript");
  internal static readonly ulong PaletteId = Id("Palette ");
  internal static readonly ulong ImageFrameId = Id("ImageFrm");
  internal static readonly ulong DifferenceFrameId = Id("DiffeFrm");

  private static readonly HashSet<ulong> _Known = [
    HeaderId, StreamId, FileHeaderId, ImageInfoId, PreviewInfoId, SoundInfoId, SequenceId,
    CopyrightId, DescriptionId, PaletteId, ImageFrameId, DifferenceFrameId,
  ];

  /// <summary>Lists the records between <paramref name="start"/> and <paramref name="end"/>.</summary>
  /// <remarks>A record whose length runs past its container is cut short at the container's end rather than trusted.</remarks>
  internal static List<Record> Records(ReadOnlySpan<byte> data, int start, int end, bool strict) {
    var records = new List<Record>();
    var at = start;
    while (at + 16 <= end) {
      var id = BinaryPrimitives.ReadUInt64LittleEndian(data[at..]);
      var length = BinaryPrimitives.ReadUInt64LittleEndian(data[(at + 8)..]);
      var body = at + 16;
      var available = (ulong)(end - body);

      // EMC lets the last record say "to the end" with a length of all ones.
      if (length is ulong.MaxValue or 0xFFFFFFFF)
        length = available;
      if (length > available) {
        // A record this reader needs that ran off the end is a truncated file; anything else
        // overrunning is padding or junk after the last record and ends the list.
        if (!_Known.Contains(id))
          break;
        if (strict)
          throw new InvalidDataException($"ERI record \"{_Name(id)}\" states {length} bytes but only {available} follow.");
        length = available;
      }

      records.Add(new(id, body, (int)length));
      at = body + (int)length;
    }

    return records;
  }

  private static string _Name(ulong id) {
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(bytes, id);
    var text = new StringBuilder(8);
    foreach (var b in bytes)
      text.Append(b is >= 0x20 and < 0x7F ? (char)b : '?');
    return text.ToString();
  }

  /// <summary>Whether the bytes start like an ERI-family file.</summary>
  internal static bool HasSignature(ReadOnlySpan<byte> data)
    => data.Length >= 12 && data[..8].SequenceEqual(Signature) && BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) == RasterizedImageId;

  /// <summary>The parsed header records, without any image data.</summary>
  internal sealed class Headers {
    public EriFileHeader FileHeader;
    public EriInfoHeader? Info;
    public EriInfoHeader? Preview;
    public bool HasSound;
    public EriSequenceEntry[]? Sequence;
    public string? Copyright;
    public string? Description;
    public int StreamOffset = -1;
    public int StreamLength;
  }

  internal static Headers ReadHeaders(ReadOnlySpan<byte> data, bool strict) {
    if (data.Length < 8 || !data[..8].SequenceEqual(Signature))
      throw new InvalidDataException("Not an ERI file: the EMC signature \"Entis\\x1A\" is missing.");
    if (data.Length < RecordsOffset)
      throw new InvalidDataException("ERI file is too short to hold its EMC header.");

    var fileId = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
    if (fileId != RasterizedImageId)
      throw new InvalidDataException($"EMC file identifier {fileId:X8} is not an ERI picture or animation (03000100H).");

    var headers = new Headers();
    var foundFileHeader = false;
    foreach (var top in Records(data, RecordsOffset, data.Length, strict)) {
      if (top.Id == HeaderId) {
        foreach (var record in Records(data, top.Offset, top.Offset + top.Length, strict)) {
          var body = data.Slice(record.Offset, record.Length);
          if (record.Id == FileHeaderId && body.Length >= EriFileHeader.Size) {
            headers.FileHeader = EriFileHeader.Parse(body);
            foundFileHeader = true;
          } else if (record.Id == ImageInfoId && body.Length >= EriInfoHeader.Size)
            headers.Info = EriInfoHeader.Parse(body);
          else if (record.Id == PreviewInfoId && body.Length >= EriInfoHeader.Size)
            headers.Preview = EriInfoHeader.Parse(body);
          else if (record.Id == SoundInfoId)
            headers.HasSound = true;
          else if (record.Id == SequenceId) {
            var entries = new EriSequenceEntry[body.Length / 8];
            for (var i = 0; i < entries.Length; ++i)
              entries[i] = new(BinaryPrimitives.ReadUInt32LittleEndian(body[(i * 8)..]), BinaryPrimitives.ReadUInt32LittleEndian(body[(i * 8 + 4)..]));
            headers.Sequence = entries;
          } else if (record.Id == CopyrightId)
            headers.Copyright = DecodeText(body);
          else if (record.Id == DescriptionId)
            headers.Description = DecodeText(body);
        }
      } else if (top.Id == StreamId) {
        headers.StreamOffset = top.Offset;
        headers.StreamLength = top.Length;
      }
    }

    if (!foundFileHeader)
      throw new InvalidDataException("ERI file has no \"FileHdr \" record.");
    if (headers.FileHeader.Version > EriInfoHeader.StandardVersion)
      throw new NotSupportedException($"ERI file version {headers.FileHeader.Version:X8} is newer than the format defines.");
    if (headers.Info is null)
      throw new InvalidDataException(headers.HasSound
        ? "ERI file holds sound only (an MIO stream), not a picture."
        : "ERI file has no \"ImageInf\" record.");

    return headers;
  }

  /// <summary>Comment and copyright text: UTF-16 behind a byte-order mark, otherwise Shift-JIS.</summary>
  internal static string DecodeText(ReadOnlySpan<byte> body) {
    if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE)
      return Encoding.Unicode.GetString(body[2..(2 + (body.Length - 2) / 2 * 2)]).TrimEnd('\0');

    var end = body.IndexOf((byte)0);
    if (end >= 0)
      body = body[..end];
    return _ShiftJis().GetString(body);
  }

  private static Encoding _ShiftJis() {
    try {
      return CodePagesEncodingProvider.Instance.GetEncoding(932) ?? Encoding.Latin1;
    } catch (ArgumentException) {
      return Encoding.Latin1;
    } catch (NotSupportedException) {
      return Encoding.Latin1;
    }
  }

  public static EriFile FromSpan(ReadOnlySpan<byte> data) {
    var headers = ReadHeaders(data, strict: true);
    var info = headers.Info!.Value;
    _Validate(info);

    if (headers.StreamOffset < 0)
      throw new InvalidDataException("ERI file has no \"Stream  \" record.");

    var colorType = ColorTypeOf(info);
    var channels = EriLosslessDecoder.Channels(info);
    var width = info.Width;
    var rows = info.Rows;
    if ((long)width * rows * channels > int.MaxValue)
      throw new InvalidDataException("ERI picture is too large to decode.");
    var sampleCount = width * rows * channels;

    byte[]? palette = null;
    var frames = new List<byte[]>();
    byte[]? previous = null;

    foreach (var record in Records(data, headers.StreamOffset, headers.StreamOffset + headers.StreamLength, strict: true)) {
      if (record.Id == PaletteId) {
        var body = data.Slice(record.Offset, record.Length);
        var entries = Math.Min(256, body.Length / 4);
        palette = new byte[256 * 3];
        for (var i = 0; i < entries; ++i) {
          palette[i * 3] = body[i * 4 + 2];
          palette[i * 3 + 1] = body[i * 4 + 1];
          palette[i * 3 + 2] = body[i * 4];
        }
        continue;
      }

      var difference = record.Id == DifferenceFrameId;
      if (record.Id != ImageFrameId && !difference)
        continue;

      var samples = new byte[sampleCount];
      if (info.Transformation != EriInfoHeader.LosslessTransformation) {
        if (difference)
          throw new NotSupportedException("Lossy ERI difference frames (motion-compensated MEI video) are not decoded by this reader.");
        if (colorType == EriColorType.Palette)
          throw new InvalidDataException("ERI lossy picture cannot be paletted.");
        EriLossyDecoder.Decode(data.Slice(record.Offset, record.Length).ToArray(), info, samples, channels);
        previous = samples;
        frames.Add(_ToPixels(samples, info, colorType, channels));
        continue;
      }

      if (difference) {
        if (previous is null)
          throw new InvalidDataException("ERI difference frame has no frame before it to apply to.");
        previous.CopyTo(samples, 0);
      }

      EriLosslessDecoder.Decode(data.Slice(record.Offset, record.Length).ToArray(), info, samples, difference);
      previous = samples;
      frames.Add(_ToPixels(samples, info, colorType, channels));
    }

    if (frames.Count == 0)
      throw new InvalidDataException("ERI file's stream holds no image frame.");
    if (colorType == EriColorType.Palette && palette is null)
      throw new InvalidDataException("ERI palette picture has no \"Palette \" record.");

    return new() {
      FileHeader = headers.FileHeader,
      Info = info,
      Palette = palette,
      Copyright = headers.Copyright,
      Description = headers.Description,
      Sequence = headers.Sequence,
      Frames = frames,
    };
  }

  internal static EriColorType ColorTypeOf(in EriInfoHeader info) {
    var type = info.FormatType & EriInfoHeader.TypeMask;
    if (type == EriInfoHeader.GrayImage)
      return EriColorType.Gray;
    if (type != EriInfoHeader.RgbImage)
      throw new NotSupportedException($"ERI colour type {info.FormatType:X8} is not one the format defines.");
    if (info.BitsPerPixel <= 8)
      return EriColorType.Palette;
    return (info.FormatType & EriInfoHeader.WithAlpha) != 0 ? EriColorType.Rgba : EriColorType.Rgb;
  }

  private static void _Validate(in EriInfoHeader info) {
    if (info.Width < 1 || info.Width > MaxDimension || info.Rows < 1 || info.Rows > MaxDimension)
      throw new InvalidDataException($"ERI picture states a size of {info.Width}x{info.Height}.");

    var colorType = ColorTypeOf(info);
    var valid = colorType switch {
      EriColorType.Rgb => info.BitsPerPixel is 16 or 24 or 32,
      EriColorType.Rgba => info.BitsPerPixel == 32,
      _ => info.BitsPerPixel == 8,
    };
    if (!valid)
      throw new InvalidDataException($"ERI {colorType} picture cannot be {info.BitsPerPixel} bits deep.");

    // The ERISA library writes 00020300H in front of its lossy pictures.
    var lossy = info.Transformation != EriInfoHeader.LosslessTransformation;
    if (info.Version is not (EriInfoHeader.StandardVersion or EriInfoHeader.ExtendedVersion) && !(lossy && info.Version == EriInfoHeader.LossyVersion))
      throw new NotSupportedException($"ERI information header version {info.Version:X8} is not one the format defines.");
    if (info.Transformation is not (EriInfoHeader.LosslessTransformation or EriInfoHeader.DctTransformation or EriInfoHeader.LotTransformation))
      throw new NotSupportedException($"ERI transformation {info.Transformation:X8} is not one the format defines.");
  }

  /// <summary>Turns decoded samples into top-down pixels in the order <see cref="EriFile.ToRawImage(EriFile)"/> hands out.</summary>
  private static byte[] _ToPixels(byte[] samples, in EriInfoHeader info, EriColorType colorType, int channels) {
    var width = info.Width;
    var rows = info.Rows;
    var outChannels = colorType switch { EriColorType.Rgba => 4, EriColorType.Rgb => 3, _ => 1 };
    var pixels = new byte[width * rows * outChannels];
    var fiveBit = colorType == EriColorType.Rgb && info.BitsPerPixel == 16;

    for (var y = 0; y < rows; ++y) {
      var sourceRow = (info.TopDown ? y : rows - 1 - y) * width * channels;
      var targetRow = y * width * outChannels;
      if (!fiveBit) {
        samples.AsSpan(sourceRow, width * channels).CopyTo(pixels.AsSpan(targetRow));
        continue;
      }

      // A 16-bit picture's channels are five bits each; widened by repeating the top bits.
      for (var i = 0; i < width * 3; ++i) {
        var five = samples[sourceRow + i] & 0x1F;
        pixels[targetRow + i] = (byte)((five << 3) | (five >> 2));
      }
    }

    return pixels;
  }
}
