using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.H263Video;

/// <summary>A raw H.263 elementary video stream, split at byte-aligned picture start codes.</summary>
[FormatMimeType("video/H263", "video/h263", "video/x-h263")]
public sealed class H263VideoContainer : IVideoContainerReader<H263VideoContainer> {

  private static readonly MediaStreamInfo[] _STREAM = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("H263"),
      CodecId = "H263",
    },
  ];

  public required ReadOnlyMemory<byte> Data { get; init; }

  public static string PrimaryExtension => ".263";
  public static string[] FileExtensions => [".263", ".h263"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => _IsPictureStart(header, 0) ? true : null;

  public static H263VideoContainer FromSpan(ReadOnlySpan<byte> data) {
    if (!_IsPictureStart(data, 0))
      throw new InvalidDataException("The stream does not begin with an H.263 picture start code.");

    return new() { Data = data.ToArray() };
  }

  public static H263VideoContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (!_IsPictureStart(data, 0))
      throw new InvalidDataException("The stream does not begin with an H.263 picture start code.");

    return new() { Data = data };
  }

  public static H263VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("H.263 video file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(H263VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return _STREAM;
  }

  public static IEnumerable<CodedPacket> ReadPackets(H263VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var data = container.Data;
    var start = 0;
    long picture = 0;
    while (start < data.Length) {
      var next = _FindNextPictureStart(data, start + 3);
      var end = next < 0 ? data.Length : next;
      yield return new(
        0,
        data.Slice(start, end - start),
        PresentationTimestamp: picture,
        DecodeTimestamp: picture,
        Duration: 1,
        IsKeyFrame: false);

      if (next < 0)
        yield break;
      start = next;
      ++picture;
    }
  }

  public static IEnumerable<CodedPacket> ReadPackets(H263VideoContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  public static VideoMetadata Metadata(H263VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return new() { Streams = [new(0, MediaStreamKind.Video, _STREAM[0].Codec)] };
  }

  internal static bool IsPictureStart(ReadOnlySpan<byte> data)
    => _IsPictureStart(data, 0);

  private static bool _IsPictureStart(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset <= data.Length - 3
      && data[offset] == 0
      && data[offset + 1] == 0
      && (data[offset + 2] & 0xFC) == 0x80;

  private static int _FindNextPictureStart(ReadOnlyMemory<byte> data, int offset) {
    var span = data.Span;
    for (var i = Math.Max(0, offset); i <= span.Length - 3; ++i)
      if (_IsPictureStart(span, i))
        return i;

    return -1;
  }
}
