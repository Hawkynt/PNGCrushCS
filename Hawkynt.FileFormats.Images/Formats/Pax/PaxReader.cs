using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Pax;

/// <summary>Reads Pick Ax (PAX) pictures, which need the password they were locked with.</summary>
/// <remarks>
/// The layout, after the 3-byte tag and the 128-byte identifier:
/// <list type="number">
/// <item>An 80-byte header (header length, width, height, bytes per pixel, palette entries, the
/// encryption type as a little-endian version word, a day/month/year date, two 32-byte names),
/// plain Blowfish.</item>
/// <item>For a paletted picture, the palette as R, G, B, pad quads, plain Blowfish.</item>
/// <item>Tags up to the header length: an 8-byte name-and-size record then the data padded to eight
/// bytes, plain Blowfish. Smaller Animals' libraries state the padded size and skip exactly what is
/// stated; a smaller stated size is still read here, by rounding it up.</item>
/// <item>Each row: an 8-byte record of compressed and encrypted lengths, then the row compressed as
/// a zlib stream of its own, zero-padded to eight bytes, both encrypted with the type's cipher.</item>
/// </list>
/// A wrong password is only noticed as a header that makes no sense, which is how Smaller Animals'
/// own reader notices it too.
/// </remarks>
public static class PaxReader {

  /// <summary>The tag, the identifier and the header: the least a PAX can be.</summary>
  internal const int MinimumSize = 3 + PaxFile.FileIdSize + HeaderSize;

  /// <summary>Bytes in the fixed header.</summary>
  internal const int HeaderSize = 80;

  /// <summary>Reads a PAX locked with the empty password, and refuses any other by name.</summary>
  public static PaxFile FromFile(FileInfo file) => FromFile(file, string.Empty);

  /// <summary>Reads a PAX with its password.</summary>
  public static PaxFile FromFile(FileInfo file, string password) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PAX file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName), password);
  }

  /// <inheritdoc cref="FromFile(FileInfo)"/>
  public static PaxFile FromStream(Stream stream) => FromSpan(StreamBytes.ReadAll(stream), string.Empty);

  /// <inheritdoc cref="FromFile(FileInfo, string)"/>
  public static PaxFile FromStream(Stream stream, string password) => FromSpan(StreamBytes.ReadAll(stream), password);

  /// <inheritdoc cref="FromFile(FileInfo)"/>
  public static PaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, string.Empty);
  }

  /// <inheritdoc cref="FromFile(FileInfo, string)"/>
  public static PaxFile FromBytes(byte[] data, string password) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, password);
  }

  /// <inheritdoc cref="FromFile(FileInfo)"/>
  public static PaxFile FromSpan(ReadOnlySpan<byte> data) => _Read(data, string.Empty, passwordGiven: false);

  /// <inheritdoc cref="FromFile(FileInfo, string)"/>
  public static PaxFile FromSpan(ReadOnlySpan<byte> data, string password) {
    ArgumentNullException.ThrowIfNull(password);
    return _Read(data, password, passwordGiven: true);
  }

  /// <summary>Whether <paramref name="password"/> opens <paramref name="data"/>, without decoding the rows.</summary>
  public static bool IsPassword(ReadOnlySpan<byte> data, string password) {
    ArgumentNullException.ThrowIfNull(password);
    if (!_HasTag(data) || data.Length < MinimumSize)
      return false;

    return _ReadHeader(data, new(password)) != null;
  }

  private static bool _HasTag(ReadOnlySpan<byte> data)
    => data.Length >= 3 && data[0] == (byte)'P' && data[1] == (byte)'A' && data[2] == (byte)'X';

  private sealed record Header(
    int HeaderLength, int Width, int Height, PaxColorMode ColorMode, int PaletteCount,
    PaxEncryption Encryption, DateOnly? Date, string ApplicationName, string AuthorName);

  private static Header? _ReadHeader(ReadOnlySpan<byte> data, PaxCipher cipher) {
    Span<byte> header = stackalloc byte[HeaderSize];
    data.Slice(3 + PaxFile.FileIdSize, HeaderSize).CopyTo(header);
    cipher.DecryptHeader(header);

    int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(header);
    int width = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
    int height = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
    int bytesPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
    int paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
    int version = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);

    if (headerLength < HeaderSize || 3 + PaxFile.FileIdSize + headerLength > data.Length)
      return null;
    if (width == 0 || height == 0 || bytesPerPixel is < 1 or > 3 || version is < 1 or > 6)
      return null;

    var colorMode = (PaxColorMode)bytesPerPixel;
    if (colorMode == PaxColorMode.Indexed8) {
      if (paletteCount is < 1 or > 256 || HeaderSize + PaletteBytes(paletteCount) > headerLength)
        return null;
    } else
      paletteCount = 0;

    return new(
      headerLength, width, height, colorMode, paletteCount, (PaxEncryption)version,
      _Date(header[12], header[13], BinaryPrimitives.ReadUInt16LittleEndian(header[14..])),
      _Name(header.Slice(16, 32)), _Name(header.Slice(48, 32)));
  }

  /// <summary>Palette bytes as stored: four a colour, rounded up to whole cipher blocks.</summary>
  internal static int PaletteBytes(int count) => (count * 4 + 7) & ~7;

  private static DateOnly? _Date(int day, int month, int year)
    => year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
      ? new DateOnly(year, month, day)
      : null;

  private static string _Name(ReadOnlySpan<byte> field) {
    var end = field.IndexOf((byte)0);
    return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
  }

  private static PaxFile _Read(ReadOnlySpan<byte> data, string password, bool passwordGiven) {
    if (!_HasTag(data))
      throw new InvalidDataException("Not a Pick Ax (PAX) file: it does not start with \"PAX\".");
    if (data.Length < MinimumSize)
      throw new InvalidDataException("Pick Ax (PAX) file is too short to hold its identifier and header.");

    var cipher = new PaxCipher(password);
    var header = _ReadHeader(data, cipher)
      ?? throw new InvalidDataException(passwordGiven
        ? "This Pick Ax (PAX) picture is password-protected and the password given does not open it."
        : "This Pick Ax (PAX) picture is password-protected; read it with PaxReader.FromBytes(data, password).");

    var position = 3 + PaxFile.FileIdSize + HeaderSize;
    var headerEnd = 3 + PaxFile.FileIdSize + header.HeaderLength;

    byte[]? palette = null;
    if (header.ColorMode == PaxColorMode.Indexed8) {
      var stored = data.Slice(position, PaletteBytes(header.PaletteCount)).ToArray();
      cipher.DecryptHeader(stored);
      palette = new byte[header.PaletteCount * 3];
      for (var i = 0; i < header.PaletteCount; ++i) {
        palette[3 * i] = stored[4 * i];
        palette[3 * i + 1] = stored[4 * i + 1];
        palette[3 * i + 2] = stored[4 * i + 2];
      }

      position += stored.Length;
    }

    var tags = new List<PaxTag>();
    Span<byte> record = stackalloc byte[8];
    while (position + 8 <= headerEnd) {
      data.Slice(position, 8).CopyTo(record);
      cipher.DecryptHeader(record);
      position += 8;
      var size = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
      var padded = (size + 7) & ~7L;
      if (padded > headerEnd - position)
        throw new InvalidDataException($"Pick Ax (PAX) tag of {size} bytes runs past the header.");

      var body = data.Slice(position, (int)padded).ToArray();
      cipher.DecryptHeader(body);
      tags.Add(new(Encoding.Latin1.GetString(record[..PaxTag.NameSize]), body[..(int)size]));
      position += (int)padded;
    }

    position = headerEnd;
    var stride = header.Width * (int)header.ColorMode;
    var pixels = new byte[stride * header.Height];
    for (var y = 0; y < header.Height; ++y) {
      if (position + 8 > data.Length)
        throw new InvalidDataException($"Pick Ax (PAX) file ends before row {y}.");

      data.Slice(position, 8).CopyTo(record);
      cipher.Decrypt(header.Encryption, record);
      position += 8;
      var compressed = BinaryPrimitives.ReadUInt32LittleEndian(record);
      var encrypted = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
      if (compressed > encrypted || encrypted > data.Length - position)
        throw new InvalidDataException($"Pick Ax (PAX) row {y} states lengths the file cannot hold.");

      var row = data.Slice(position, (int)encrypted).ToArray();
      cipher.Decrypt(header.Encryption, row);
      position += (int)encrypted;
      _Inflate(row.AsSpan(0, (int)compressed), pixels.AsSpan(y * stride, stride), y);
    }

    return new() {
      Width = header.Width,
      Height = header.Height,
      ColorMode = header.ColorMode,
      Encryption = header.Encryption,
      Password = password,
      FileId = data.Slice(3, PaxFile.FileIdSize).ToArray(),
      ApplicationName = header.ApplicationName,
      AuthorName = header.AuthorName,
      Date = header.Date,
      Palette = palette,
      PaletteCount = header.PaletteCount,
      Tags = tags,
      PixelData = pixels,
    };
  }

  private static void _Inflate(ReadOnlySpan<byte> compressed, Span<byte> row, int y) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      var filled = 0;
      while (filled < row.Length) {
        var read = zlib.Read(row[filled..]);
        if (read == 0)
          break;
        filled += read;
      }

      if (filled != row.Length)
        throw new InvalidDataException($"Pick Ax (PAX) row {y} inflates to {filled} bytes instead of {row.Length}.");
    } catch (InvalidDataException) {
      throw;
    } catch (Exception exception) when (exception is IOException or NotSupportedException) {
      throw new InvalidDataException($"Pick Ax (PAX) row {y} is not a zlib stream.", exception);
    }
  }
}
