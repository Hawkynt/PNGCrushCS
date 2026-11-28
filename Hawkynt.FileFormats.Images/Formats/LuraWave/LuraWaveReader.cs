using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.LuraWave;

/// <summary>Reads LuraWave files from bytes, streams, or file paths.</summary>
/// <remarks>
/// A password-protected stream decodes to its public quality (the <c>PW</c> chunk's limit) unless the overloads that
/// take a password are given the right one; with it, the stream decodes in full.
/// </remarks>
public static class LuraWaveReader {

  public static LuraWaveFile FromFile(FileInfo file) => FromFile(file, null);

  /// <param name="file">The file to read.</param>
  /// <param name="password">The access password of a protected stream, or <see langword="null"/> for its public quality.</param>
  /// <exception cref="UnauthorizedAccessException">The stream is protected and <paramref name="password"/> is wrong.</exception>
  public static LuraWaveFile FromFile(FileInfo file, string? password) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LuraWave file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), password);
  }

  public static LuraWaveFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream), null);

  /// <inheritdoc cref="FromFile(FileInfo, string?)"/>
  public static LuraWaveFile FromStream(Stream stream, string? password) => FromBytes(StreamBytes.ReadAll(stream), password);

  public static LuraWaveFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray(), null);

  /// <inheritdoc cref="FromFile(FileInfo, string?)"/>
  public static LuraWaveFile FromSpan(ReadOnlySpan<byte> data, string? password) => FromBytes(data.ToArray(), password);

  /// <summary>Parses the header and decodes every stripe.</summary>
  /// <exception cref="InvalidDataException">Not a LuraWave stream, a malformed header, or coded data shorter than declared.</exception>
  public static LuraWaveFile FromBytes(byte[] data) => FromBytes(data, null);

  /// <summary>Parses the header and decodes every stripe, past the public quality when <paramref name="password"/> unlocks the stream.</summary>
  /// <param name="data">The whole file.</param>
  /// <param name="password">
  /// The access password of a protected stream, or <see langword="null"/> (or empty) to decode it at its public
  /// quality. Ignored for a stream without password protection.
  /// </param>
  /// <exception cref="InvalidDataException">Not a LuraWave stream, a malformed header, or coded data shorter than declared.</exception>
  /// <exception cref="UnauthorizedAccessException">The stream is protected and <paramref name="password"/> is wrong.</exception>
  public static LuraWaveFile FromBytes(byte[] data, string? password) {
    ArgumentNullException.ThrowIfNull(data);
    var header = LuraWaveHeader.Parse(data);
    uint? check = null;
    if (!string.IsNullOrEmpty(password) && header.PublicQuality < LuraWaveHeader.FullQuality) {
      if (LuraWavePassword.HeaderCheck(password) != header.PasswordCheck)
        throw new UnauthorizedAccessException("The password does not unlock this LuraWave stream.");
      check = LuraWavePassword.StreamCheck(password);
    }

    return new() {
      Width = header.Width,
      Height = header.Height,
      Mode = header.Mode,
      Header = header,
      PixelData = LuraWaveCodec.Decode(data, header, check),
    };
  }
}
