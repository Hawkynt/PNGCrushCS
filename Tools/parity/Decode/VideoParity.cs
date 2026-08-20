using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

/// <summary>
/// The video half of the parity tool: the same questions the image registry is asked, asked of the
/// container and codec registries as well.
/// </summary>
/// <remarks>
/// Two registries and not one, because a container and a still are not the same kind of thing — one
/// holds streams of coded packets and the other holds a picture. Everything here therefore reports
/// alongside the image answers rather than instead of them: a name claimed on both sides is claimed
/// twice, and saying which one won would throw away the fact that there was a contest.
/// <para/>
/// The steps are kept apart in what is printed, because after the video package split demuxing from
/// decoding they fail separately and for different reasons. A file that opens and then has no codec
/// is a codec this project has not written; a file that will not open is a container it cannot take
/// apart. Reporting both as "did not decode" is what made the old measurement useless for deciding
/// what to write next.
/// </remarks>
internal static class VideoParity {

  /// <summary>Every extension a registered container claims.</summary>
  /// <remarks>
  /// Part of the denominator of the comparison and not a separate list: a name this project reads is
  /// read whether the thing behind it is a still or a film, and counting only the stills would make
  /// every container we do read look like one we do not.
  /// </remarks>
  internal static IEnumerable<string> Extensions()
    => VideoFormatRegistry.AllFormats.SelectMany(entry => entry.AllExtensions ?? Array.Empty<string>());

  /// <summary>The containers that claim a file, by its bytes first and then by its name.</summary>
  /// <remarks>
  /// The bytes come first because they are what the writer wrote. The name is consulted as well
  /// rather than instead, because a raw Motion JPEG stream has no signature it could be recognised
  /// by — a single-frame <c>.mjpg</c> is a valid JPEG byte for byte — and a container with nothing to
  /// match on would otherwise be invisible to every one of these measurements.
  /// </remarks>
  internal static IReadOnlyList<VideoFormatEntry> ContainersClaiming(FileInfo file) {
    var found = new List<VideoFormatEntry>();

    var detected = VideoFormatRegistry.GetEntry(VideoFormatRegistry.Detect(file));
    if (detected != null)
      found.Add(detected);

    foreach (var format in VideoFormatRegistry.ByExtension(file.Extension)) {
      var entry = VideoFormatRegistry.GetEntry(format);
      if (entry != null && !found.Contains(entry))
        found.Add(entry);
    }

    return found;
  }

  /// <summary>Why a container was refused, step by step — <c>--why</c>.</summary>
  /// <remarks>
  /// Opening, choosing a codec and decoding a frame are printed as three separate outcomes because
  /// they are three separate jobs. An unsupported codec now arrives from the second of them: the
  /// container takes the file apart perfectly and reports its streams, and it is the codec lookup
  /// that has nothing to offer. That distinction is the whole point of asking.
  /// </remarks>
  internal static void Explain(FileInfo file) {
    foreach (var entry in ContainersClaiming(file)) {
      byte[] bytes;
      try {
        bytes = File.ReadAllBytes(file.FullName);
      } catch (Exception failure) {
        Console.WriteLine($"  [video] {entry.Name}: {failure.GetType().Name}: {failure.Message}");
        continue;
      }

      IReadOnlyList<MediaStreamInfo> streams;
      try {
        streams = entry.ReadStreams(bytes);
      } catch (Exception failure) {
        Console.WriteLine($"  [video] {entry.Name}: would not open — {failure.GetType().Name}: {failure.Message}");
        continue;
      }

      Console.WriteLine($"  [video] {entry.Name}: opened, {streams.Count} stream(s)");
      foreach (var stream in streams) {
        var described = $"    stream {stream.Index} {stream.Kind.ToString().ToLowerInvariant()} '{stream.Codec}'"
                        + (stream.Width > 0 ? $" {stream.Width}x{stream.Height}@{stream.BitsPerPixel}" : string.Empty);

        IVideoFrameDecoder decoder;
        try {
          decoder = VideoFormatRegistry.CreateDecoder(stream);
        } catch (Exception failure) {
          Console.WriteLine($"{described}: no codec — {failure.GetType().Name}: {failure.Message}");
          continue;
        }

        try {
          var first = _Frames(entry, bytes, stream).FirstOrDefault();
          Console.WriteLine(first.Image == null
            ? $"{described}: {decoder.GetType().Name} took the stream but it holds no packet that decodes"
            : $"{described}: {decoder.GetType().Name} → {first.Image.Width}x{first.Image.Height} {first.Image.Format}");
        } catch (Exception failure) {
          Console.WriteLine($"{described}: {decoder.GetType().Name} would not decode — {failure.GetType().Name}: {failure.Message}");
        }
      }
    }
  }

  /// <summary>Which containers would take this file if its name were not in the way — <c>--anyformat</c>.</summary>
  internal static void Describe(FileInfo file) {
    byte[] bytes;
    try {
      bytes = File.ReadAllBytes(file.FullName);
    } catch (Exception) {
      return;
    }

    foreach (var entry in VideoFormatRegistry.AllFormats) {
      IReadOnlyList<MediaStreamInfo> streams;
      try {
        streams = entry.ReadStreams(bytes);
      } catch (Exception) {
        continue;
      }

      if (streams.Count == 0)
        continue;

      var decodable = streams.Count(VideoFormatRegistry.CanDecode);
      Console.WriteLine(
        $"  [video] {entry.Name}\t{streams.Count} stream(s)\t{decodable} decodable\t{entry.PrimaryExtension}");

      foreach (var stream in streams)
        Console.WriteLine(
          $"    stream {stream.Index}\t{stream.Kind.ToString().ToLowerInvariant()}\t'{stream.Codec}'"
          + $"\t{(stream.Width > 0 ? $"{stream.Width}x{stream.Height}" : "-")}"
          + $"\t{(VideoFormatRegistry.CanDecode(stream) ? "decodable" : "no codec")}");
    }
  }

  /// <summary>
  /// Writes every frame of a container's first video stream — the <c>--frames</c> path for video.
  /// </summary>
  /// <remarks>
  /// The decoder is built before the walk begins, on purpose. Frames are reached lazily, so a stream
  /// whose codec nothing here decodes would otherwise fail on the first step of the enumeration and
  /// could just as easily have produced nothing at all — and zero frames written is exactly what a
  /// container holding no packets looks like. Asking for the decoder first makes a refusal read as a
  /// refusal.
  /// <para/>
  /// The same two files per frame as the image path, and the same names: the <c>.ppm</c> the
  /// comparison scripts expect, and a <c>.pam</c> beside it carrying the alpha the <c>.ppm</c>
  /// cannot, since an uncompressed 32-bit stream has one and it would otherwise go unmeasured.
  /// </remarks>
  internal static int WriteFrames(FileInfo source, string into, VideoFormatEntry entry) {
    var bytes = File.ReadAllBytes(source.FullName);

    IReadOnlyList<MediaStreamInfo> streams;
    try {
      streams = entry.ReadStreams(bytes);
    } catch (Exception failure) {
      Console.Error.WriteLine($"{source.Name}: {entry.Name} would not open it — {failure.GetType().Name}: {failure.Message}");
      return 1;
    }

    var video = streams.FirstOrDefault(s => s.Kind == MediaStreamKind.Video);
    if (video == null) {
      Console.Error.WriteLine($"{source.Name}: {entry.Name} holds no video stream");
      return 1;
    }

    try {
      VideoFormatRegistry.CreateDecoder(video);
    } catch (Exception failure) {
      Console.Error.WriteLine($"{source.Name}: {failure.GetType().Name}: {failure.Message}");
      return 1;
    }

    var frame = 0;
    try {
      foreach (var decoded in _Frames(entry, bytes, video)) {
        var picture = decoded.Image;
        var rgb = picture.ToRgb24();
        var rgba = picture.ToRgba32();
        var wantedRgb = (long)picture.Width * picture.Height * 3;
        var wantedRgba = (long)picture.Width * picture.Height * 4;
        if (rgb.LongLength < wantedRgb || rgba.LongLength < wantedRgba) {
          Console.Error.WriteLine($"{source.Name}: frame {frame} produced too few bytes for a {picture.Width}x{picture.Height} picture");
          return 1;
        }

        var stem = Path.Combine(into, $"{source.Name}.{frame:D2}");
        using (var file = File.Create(stem + ".ppm")) {
          file.Write(Encoding.ASCII.GetBytes($"P6\n{picture.Width} {picture.Height}\n255\n"));
          file.Write(rgb, 0, (int)wantedRgb);
        }

        using (var file = File.Create(stem + ".pam")) {
          file.Write(Encoding.ASCII.GetBytes(
            $"P7\nWIDTH {picture.Width}\nHEIGHT {picture.Height}\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n"));
          file.Write(rgba, 0, (int)wantedRgba);
        }

        Console.WriteLine($"{stem}\t{picture.Width}x{picture.Height}\t{picture.Format}\tpts={decoded.PresentationTimestamp}");
        ++frame;
      }
    } catch (Exception failure) {
      // A container that stops part way through is not a container of however many frames came out
      // before it did. Reporting the count so far would present a truncated read as a complete one.
      Console.Error.WriteLine($"{source.Name}: frame {frame} — {failure.GetType().Name}: {failure.Message}");
      return 1;
    }

    Console.WriteLine($"{entry.Name}: {frame} frame(s) from {source.Name}");
    return frame == 0 ? 1 : 0;
  }

  /// <summary>Walks one stream's pictures through the container this tool already chose.</summary>
  /// <remarks>
  /// Deliberately not <see cref="VideoFormatRegistry.DecodeFrames(byte[], int)"/>. That entry point
  /// identifies the container from the bytes it is given, which cannot work for one that has no
  /// signature to be identified by: a raw Motion JPEG stream is reached by its name, so re-deriving
  /// the container from its contents throws even though the caller is holding the right entry
  /// already. Going through the entry keeps the choice made once, where it was made with the name in
  /// hand.
  /// </remarks>
  private static IEnumerable<DecodedFrame> _Frames(VideoFormatEntry entry, byte[] bytes, MediaStreamInfo stream)
    => VideoIO.Decode(entry.ReadStreamPackets(bytes, stream.Index), stream, VideoFormatRegistry.CreateDecoder);
}
