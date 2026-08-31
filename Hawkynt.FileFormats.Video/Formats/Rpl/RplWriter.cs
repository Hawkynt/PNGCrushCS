using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Rpl;

/// <summary>Writes ARMovie/RPL's text header, contiguous chunk data and text chunk catalogue.</summary>
public sealed class RplWriter : IVideoContainerWriter<RplWriter> {

  private sealed class Chunk {
    internal required ReadOnlyMemory<byte> Video { get; init; }
    internal ReadOnlyMemory<byte> Audio { get; set; }
  }

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly VideoMetadata _metadata;
  private readonly List<Chunk> _chunks = [];
  private bool _finished;

  private RplWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2)
      throw new NotSupportedException("ARMovie/RPL contains one video stream and at most one audio stream.");

    var video = streams[0];
    if (video.Index != 0 || video.Kind != MediaStreamKind.Video || video.Codec.Value == 0 || video.Width <= 0 || video.Height <= 0)
      throw new NotSupportedException("RPL stream zero must be a sized video stream with a nonzero numeric codec id.");
    if (!video.FrameRate.IsKnown)
      throw new NotSupportedException("RPL writes a decimal frame rate in its header and therefore needs a known video frame rate.");

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio || audio.Codec.Value == 0)
        throw new NotSupportedException("RPL's optional second stream must be audio with a nonzero numeric codec id.");
      _AudioRate(audio); // validate now, not after packets have been accepted
    }

    this._streams = streams;
    this._metadata = metadata;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".rpl";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".rpl"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static RplWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("RPL writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared RPL stream.");

    if (packet.StreamIndex == 0) {
      if (packet.Data.IsEmpty)
        throw new InvalidDataException("RPL video chunks may not be empty.");
      this._chunks.Add(new() { Video = packet.Data });
      return;
    }

    if (this._chunks.Count == 0)
      throw new InvalidDataException("An RPL audio packet arrived before its chunk's video packet.");
    var chunk = this._chunks[^1];
    if (!chunk.Audio.IsEmpty)
      throw new InvalidDataException("Two RPL audio packets arrived for one video chunk.");
    chunk.Audio = packet.Data;
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("RPL writer has already been finished.");
    this._finished = true;
    if (this._chunks.Count == 0)
      throw new InvalidDataException("RPL needs at least one video chunk.");

    var video = this._streams[0];
    var audio = this._streams.Count == 2 ? this._streams[1] : null;
    var frameRate = _FrameRate(video.FrameRate);
    var sampleRate = audio == null ? 0 : _AudioRate(audio);
    var title = _CleanLine(this._metadata.Title) ?? "movie.rpl";
    var encodedBy = _CleanLine(this._metadata.EncodedBy) ?? "PNGCrushCS";
    var copyright = "";
    foreach (var text in this._metadata.TextEntries)
      if (text.Keyword.Equals("Copyright", StringComparison.OrdinalIgnoreCase)) {
        copyright = _CleanLine(text.Text) ?? "";
        break;
      }

    const string placeholder = "0000000000";
    var header = _Header(
      title, copyright, encodedBy,
      video, audio, frameRate, sampleRate,
      this._chunks.Count - 1, placeholder);
    var headerBytes = Encoding.Latin1.GetBytes(header);

    long dataLength = 0;
    foreach (var chunk in this._chunks)
      dataLength = checked(dataLength + chunk.Video.Length + chunk.Audio.Length);
    var catalogueOffset = checked((long)headerBytes.Length + dataLength);
    if (catalogueOffset > 9_999_999_999L)
      throw new NotSupportedException("RPL's fixed-width catalogue offset exceeds ten decimal digits.");

    header = _Header(
      title, copyright, encodedBy,
      video, audio, frameRate, sampleRate,
      this._chunks.Count - 1,
      catalogueOffset.ToString("D10", CultureInfo.InvariantCulture));
    headerBytes = Encoding.Latin1.GetBytes(header);

    using var output = new MemoryStream();
    output.Write(headerBytes);

    var entries = new (long Offset, int Video, int Audio)[this._chunks.Count];
    for (var i = 0; i < this._chunks.Count; ++i) {
      var chunk = this._chunks[i];
      entries[i] = (output.Position, chunk.Video.Length, chunk.Audio.Length);
      output.Write(chunk.Video.Span);
      output.Write(chunk.Audio.Span);
    }

    if (output.Position != catalogueOffset)
      throw new InvalidOperationException("RPL header length changed while patching its catalogue offset.");

    foreach (var entry in entries) {
      ContainerWriterTools.WriteAscii(output, entry.Offset.ToString(CultureInfo.InvariantCulture));
      output.WriteByte((byte)',');
      ContainerWriterTools.WriteAscii(output, entry.Video.ToString(CultureInfo.InvariantCulture));
      output.WriteByte((byte)';');
      ContainerWriterTools.WriteAscii(output, entry.Audio.ToString(CultureInfo.InvariantCulture));
      output.WriteByte((byte)'\n');
    }

    return output.ToArray();
  }

  private static string _Header(
    string title, string copyright, string encodedBy,
    MediaStreamInfo video, MediaStreamInfo? audio,
    string frameRate, int sampleRate, int highestChunk, string catalogueOffset) {
    var lines = new[] {
      "ARMovie",
      title,
      copyright,
      encodedBy,
      $"{video.Codec.Value}        video format",
      $"{video.Width}        pixels",
      $"{video.Height}        pixels",
      $"{(video.BitsPerPixel > 0 ? video.BitsPerPixel : 16)}         bits per pixel RGB",
      $"{frameRate}  frames per second",
      $"{audio?.Codec.Value ?? 0}          sound format",
      $"{sampleRate}          Hz samples",
      $"{(audio?.Channels is > 0 ? audio.Channels : audio == null ? 0 : 1)}          channels",
      $"{(audio?.BitsPerSample is > 0 ? audio.BitsPerSample : audio == null ? 0 : 16)}         bits per sample",
      "1          frames per chunk",
      $"{highestChunk}          number of chunks",
      "0          even chunk size",
      "0          odd chunk size",
      $"{catalogueOffset} offset to chunk cat",
      "0          offset to sprite",
      "0          size of sprite",
      "0          offset to key frames",
    };
    return string.Join('\n', lines) + "\n";
  }

  private static string _FrameRate(Rational rate) {
    const long scale = 1_000_000;
    var scaledNumerator = checked((Int128)rate.Numerator * scale);
    if (rate.Denominator <= 0 || scaledNumerator % rate.Denominator != 0)
      throw new NotSupportedException(
        $"RPL's decimal frame-rate field cannot represent {rate} exactly to six decimal places.");
    var scaled = checked((long)(scaledNumerator / rate.Denominator));
    var whole = scaled / scale;
    var fraction = Math.Abs(scaled % scale);
    return $"{whole}.{fraction:D6}";
  }

  private static int _AudioRate(MediaStreamInfo audio) {
    if (audio.SampleRate > 0)
      return audio.SampleRate;
    if (audio.TimeBase.IsKnown && audio.TimeBase.Numerator == 1
        && audio.TimeBase.Denominator is > 0 and <= int.MaxValue)
      return checked((int)audio.TimeBase.Denominator);
    throw new NotSupportedException("RPL audio needs a sample rate for its text header.");
  }

  private static string? _CleanLine(string? value) {
    if (string.IsNullOrWhiteSpace(value))
      return null;
    var line = value.Trim();
    if (line.IndexOfAny(['\r', '\n']) >= 0)
      throw new NotSupportedException("RPL text header values may not contain line breaks.");
    return line;
  }
}
