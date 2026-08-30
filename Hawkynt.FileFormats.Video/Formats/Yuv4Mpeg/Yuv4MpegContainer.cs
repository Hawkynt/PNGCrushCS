using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Yuv4Mpeg;

/// <summary>A YUV4MPEG2 stream containing uncompressed planar YUV frames.</summary>
[FormatMimeType("video/x-yuv4mpeg")]
public sealed class Yuv4MpegContainer : IVideoContainerReader<Yuv4MpegContainer> {

  private static readonly byte[] _SIGNATURE = "YUV4MPEG2"u8.ToArray();

  public required ReadOnlyMemory<byte> File { get; init; }
  public required MediaStreamInfo Stream { get; init; }
  public required string Chroma { get; init; }
  // Not `required`: a required member may not be less visible than its type, and these two are
  // parser bookkeeping rather than part of the public shape. FromBytes is the only thing that
  // constructs the container and it sets both.
  internal int FirstFrameOffset { get; init; }
  internal int FrameSize { get; init; }

  public static string PrimaryExtension => ".y4m";
  public static string[] FileExtensions => [".y4m"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= _SIGNATURE.Length && header[.._SIGNATURE.Length].SequenceEqual(_SIGNATURE) ? true : null;

  public static Yuv4MpegContainer FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Yuv4MpegContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _SIGNATURE.Length + 1 || !data.AsSpan(0, _SIGNATURE.Length).SequenceEqual(_SIGNATURE))
      throw new InvalidDataException("The file does not begin with the YUV4MPEG2 signature.");

    var headerEnd = Array.IndexOf(data, (byte)'\n');
    if (headerEnd < 0)
      throw new InvalidDataException("The YUV4MPEG2 stream header is not terminated by a line feed.");

    var header = Encoding.ASCII.GetString(data, 0, headerEnd);
    var tokens = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length == 0 || tokens[0] != "YUV4MPEG2")
      throw new InvalidDataException("Invalid YUV4MPEG2 stream header.");

    var width = 0;
    var height = 0;
    var frameRate = Rational.Unknown;
    var chroma = "420jpeg";

    foreach (var token in tokens.AsSpan(1)) {
      if (token.Length < 2)
        continue;
      switch (token[0]) {
        case 'W':
          if (!int.TryParse(token.AsSpan(1), out width) || width <= 0)
            throw new InvalidDataException($"Invalid YUV4MPEG2 width token '{token}'.");
          break;
        case 'H':
          if (!int.TryParse(token.AsSpan(1), out height) || height <= 0)
            throw new InvalidDataException($"Invalid YUV4MPEG2 height token '{token}'.");
          break;
        case 'F':
          frameRate = _ParseRatio(token.AsSpan(1), "frame rate");
          break;
        case 'C':
          chroma = token[1..];
          if (chroma.Length == 0)
            throw new InvalidDataException("The YUV4MPEG2 chroma token is empty.");
          break;
      }
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("A YUV4MPEG2 stream must declare positive W and H dimensions.");

    var frameSize = GetFrameSize(width, height, chroma);
    var bitsPerPixel = checked((frameSize * 8 + width * height - 1) / (width * height));
    var timeBase = frameRate.IsKnown ? new Rational(frameRate.Denominator, frameRate.Numerator) : Rational.Unknown;
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("YUV "),
      CodecId = "rawvideo",
      TimeBase = timeBase,
      FrameRate = frameRate,
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = Encoding.ASCII.GetBytes(chroma),
    };

    return new() {
      File = data,
      Stream = stream,
      Chroma = chroma,
      FirstFrameOffset = headerEnd + 1,
      FrameSize = frameSize,
    };
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(Yuv4MpegContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return [container.Stream];
  }

  public static IEnumerable<CodedPacket> ReadPackets(Yuv4MpegContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var file = container.File;
    var offset = container.FirstFrameOffset;
    long frame = 0;
    while (offset < file.Length) {
      var lineLength = _FrameHeaderLength(file, offset);
      var dataOffset = checked(offset + lineLength + 1);
      if (container.FrameSize > file.Length - dataOffset)
        throw new InvalidDataException($"YUV4MPEG2 frame {frame} is truncated: expected {container.FrameSize} bytes, found {file.Length - dataOffset}.");

      var privateData = lineLength == 5
        ? ReadOnlyMemory<byte>.Empty
        : file.Slice(offset + 5, lineLength - 5);
      yield return new(
        0,
        file.Slice(dataOffset, container.FrameSize),
        PresentationTimestamp: frame,
        DecodeTimestamp: frame,
        Duration: 1,
        IsKeyFrame: true,
        ContainerPrivateData: privateData);

      offset = checked(dataOffset + container.FrameSize);
      ++frame;
    }
  }

  internal static int GetFrameSize(int width, int height, string chroma) {
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "YUV frame dimensions must be positive.");

    var bytesPerSample = 1;
    var baseChroma = chroma;
    var p = chroma.LastIndexOf('p');
    if (p > 0 && int.TryParse(chroma.AsSpan(p + 1), out var depth)) {
      if (depth is not (9 or 10 or 12 or 14 or 16))
        throw new NotSupportedException($"YUV4MPEG2 chroma mode '{chroma}' uses an unsupported sample depth.");
      bytesPerSample = 2;
      baseChroma = chroma[..p];
    }

    long samples = checked((long)width * height);
    switch (baseChroma) {
      case "mono":
        break;
      case "420":
      case "420jpeg":
      case "420mpeg2":
      case "420paldv":
        samples = checked(samples + 2L * ((width + 1) / 2) * ((height + 1) / 2));
        break;
      case "411":
        samples = checked(samples + 2L * ((width + 3) / 4) * height);
        break;
      case "422":
        samples = checked(samples + 2L * ((width + 1) / 2) * height);
        break;
      case "444":
        samples = checked(samples * 3);
        break;
      case "444alpha":
        samples = checked(samples * 4);
        break;
      default:
        throw new NotSupportedException($"YUV4MPEG2 chroma mode '{chroma}' is not supported yet.");
    }

    return checked((int)(samples * bytesPerSample));
  }

  private static int _FrameHeaderLength(ReadOnlyMemory<byte> file, int offset) {
    var remaining = file.Span[offset..];
    var lineLength = remaining.IndexOf((byte)'\n');
    if (lineLength < 0)
      throw new InvalidDataException($"The YUV4MPEG2 frame header at offset {offset} is not terminated by a line feed.");
    if (lineLength < 5 || !remaining[..5].SequenceEqual("FRAME"u8) || (lineLength > 5 && remaining[5] != (byte)' '))
      throw new InvalidDataException($"Expected a YUV4MPEG2 FRAME marker at offset {offset}.");

    return lineLength;
  }

  private static Rational _ParseRatio(ReadOnlySpan<char> text, string field) {
    var separator = text.IndexOf(':');
    if (separator <= 0 || separator == text.Length - 1
        || !long.TryParse(text[..separator], out var numerator)
        || !long.TryParse(text[(separator + 1)..], out var denominator)
        || numerator < 0 || denominator < 0)
      throw new InvalidDataException($"Invalid YUV4MPEG2 {field} ratio '{text.ToString()}'.");

    return numerator == 0 || denominator == 0 ? Rational.Unknown : new(numerator, denominator);
  }
}
