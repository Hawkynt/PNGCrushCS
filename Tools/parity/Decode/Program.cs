using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

if (args.Length < 1) {
  Console.Error.WriteLine("usage: Decode <sample directory> <output directory>");
  Console.Error.WriteLine("       Decode --implausible <sample directory>");
  Console.Error.WriteLine("       Decode --extensions");
  Console.Error.WriteLine("       Decode --frames <file> <output directory>");
  return 2;
}

// Every extension anything here claims, for comparing against a tool that publishes a list rather
// than being installable — which is the only way to measure one that runs as somebody's web service.
if (args[0] == "--extensions") {
  foreach (var extension in FormatRegistry.AllFormats
             .SelectMany(entry => entry.AllExtensions ?? Array.Empty<string>())
             .Select(x => x.ToLowerInvariant())
             .Distinct()
             .OrderBy(x => x, StringComparer.Ordinal))
    Console.WriteLine(extension);

  return 0;
}

// Why a file was refused, rather than only that it was.
//
// The ordinary decode answers null for every kind of failure, which tells a caller nothing about
// which of them happened. A wrong length, a signature that is not this format's, and a depth nobody
// implemented are three different jobs, and telling them apart is most of the work of fixing one.
//
//   Decode --why <file>...
// Which readers would take this file if its name were not in the way.
//
// A good share of what we cannot read is a format we already decode perfectly, reached under a name
// somebody else's format holds. --why only asks the formats claiming the extension, so it cannot see
// those. This asks every reader in the registry and reports the ones that accept, along with the
// size each makes of it — which is enough to tell a real match from a coincidence.
//
//   Decode --anyformat <file>...
if (args[0] == "--anyformat") {
  foreach (var path in args[1..]) {
    var file = new FileInfo(path);
    Console.WriteLine($"{file.Name} ({file.Length} bytes)");

    foreach (var entry in FormatRegistry.AllFormats) {
      if (entry.LoadRawImageOrThrow == null)
        continue;

      RawImage? image;
      try {
        image = entry.LoadRawImageOrThrow(file);
      } catch (Exception) {
        continue;
      }

      if (image != null)
        Console.WriteLine($"  {entry.Name}\t{image.Width}x{image.Height}\t{image.Format}\t{entry.PrimaryExtension}");
    }
  }

  return 0;
}

if (args[0] == "--why") {
  foreach (var path in args[1..]) {
    var file = new FileInfo(path);
    var entries = FormatRegistry.AllFormats
      .Where(entry => entry.AllExtensions?.Any(e => string.Equals(e, file.Extension, StringComparison.OrdinalIgnoreCase)) == true)
      .ToArray();

    Console.WriteLine($"{file.Name} ({file.Length} bytes) — {entries.Length} format(s) claim {file.Extension}");
    foreach (var entry in entries) {
      if (entry.LoadRawImageOrThrow == null) {
        Console.WriteLine($"  {entry.Name}: no throwing entry point");
        continue;
      }

      try {
        var image = entry.LoadRawImageOrThrow(file);
        Console.WriteLine($"  {entry.Name}: {(image == null ? "returned nothing" : $"{image.Width}x{image.Height} {image.Format}")}");
      } catch (Exception failure) {
        Console.WriteLine($"  {entry.Name}: {failure.GetType().Name}: {failure.Message}");
      }
    }
  }

  return 0;
}

// Every frame of an animation, rather than the one a still viewer would draw.
//
// The ordinary decode answers a single picture, which for an animation is its first frame — so an
// animation read as a single frame and an animation whose later frames are composited wrongly look
// identical from here, and both look like success. Writing every frame is what lets each be handed
// to ffmpeg's or ImageMagick's or libwebp's own output for the same file and compared.
//
//   Decode --frames <file> <output directory>
//
// Writes <output directory>/<name>.NN.pam and .NN.ppm, numbered from zero in playing order.
//
// Both, because the two answer different questions. The .ppm is what the rest of this tool emits and
// what the comparison scripts expect, and it has no alpha — which for an animation hides the one
// thing most likely to be wrong, since a frame that disposes its rectangle leaves transparency
// behind and nothing else. The .pam carries all four channels so that transparency can be compared
// rather than assumed.
if (args[0] == "--frames") {
  if (args.Length < 3) {
    Console.Error.WriteLine("usage: Decode --frames <file> <output directory>");
    return 2;
  }

  var animation = new FileInfo(args[1]);
  var into = args[2];
  Directory.CreateDirectory(into);

  var format = FormatRegistry.DetectFromFile(animation);
  var formatEntry = FormatRegistry.GetEntry(format);
  if (formatEntry == null) {
    Console.Error.WriteLine($"no format claims {animation.Name}");
    return 2;
  }

  // A format that does not enumerate frames still has one picture, and saying so beats pretending
  // the question was refused.
  var count = formatEntry.GetImageCount?.Invoke(animation) ?? 1;
  for (var frame = 0; frame < count; ++frame) {
    var picture = formatEntry.LoadRawImageAtIndex?.Invoke(animation, frame)
                  ?? formatEntry.LoadRawImage(animation);
    if (picture == null) {
      Console.Error.WriteLine($"{animation.Name}: frame {frame} did not decode");
      return 1;
    }

    var rgb = picture.ToRgb24();
    var rgba = picture.ToRgba32();
    var wantedRgb = (long)picture.Width * picture.Height * 3;
    var wantedRgba = (long)picture.Width * picture.Height * 4;
    if (rgb.LongLength < wantedRgb || rgba.LongLength < wantedRgba) {
      Console.Error.WriteLine($"{animation.Name}: frame {frame} produced too few bytes for a {picture.Width}x{picture.Height} picture");
      return 1;
    }

    var stem = Path.Combine(into, $"{animation.Name}.{frame:D2}");
    using (var file = File.Create(stem + ".ppm")) {
      file.Write(Encoding.ASCII.GetBytes($"P6\n{picture.Width} {picture.Height}\n255\n"));
      file.Write(rgb, 0, (int)wantedRgb);
    }

    using (var file = File.Create(stem + ".pam")) {
      file.Write(Encoding.ASCII.GetBytes(
        $"P7\nWIDTH {picture.Width}\nHEIGHT {picture.Height}\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n"));
      file.Write(rgba, 0, (int)wantedRgba);
    }

    Console.WriteLine($"{stem}\t{picture.Width}x{picture.Height}\t{picture.Format}");
  }

  Console.WriteLine($"{formatEntry.Name}: {count} frame(s) from {animation.Name}");
  return 0;
}

// Which formats can be read but not written, so the list of what is left to encode is measured
// rather than remembered. The capabilities say what a format can hold, which is most of what decides
// whether writing it is a job at all: a fixed-palette machine format needs a quantiser behind it,
// where a truecolour one only needs its bytes laid out.
if (args[0] == "--readonly") {
  var readOnly = FormatRegistry.AllFormats
    .Where(entry => !entry.SupportsWrite)
    .OrderBy(entry => entry.Name, StringComparer.Ordinal)
    .ToArray();

  foreach (var entry in readOnly)
    Console.WriteLine($"{entry.Name}\t{entry.PrimaryExtension}\t{entry.Capabilities}\t{string.Join(',', entry.AllExtensions ?? Array.Empty<string>())}");

  Console.WriteLine($"{readOnly.Length} of {FormatRegistry.AllFormats.Count()} formats read but do not write");
  return 0;
}

// Encodes one picture into a named format, so that what a writer produces can be handed to the same
// third-party tools the readers are measured against. A writer is only known to work when something
// that is not this project reads its output back.
//
//   Decode --encode <format> <source picture> <output file>
if (args[0] == "--encode") {
  if (args.Length < 4) {
    Console.Error.WriteLine("usage: Decode --encode <format> <source picture> <output file>");
    return 2;
  }

  var wanted = FormatRegistry.AllFormats.FirstOrDefault(entry => string.Equals(entry.Name, args[1], StringComparison.OrdinalIgnoreCase));
  if (wanted == null) {
    Console.Error.WriteLine($"no format named {args[1]}");
    return 2;
  }

  if (!wanted.SupportsWrite) {
    Console.Error.WriteLine($"{wanted.Name} does not write");
    return 2;
  }

  var source = FormatRegistry.Read(new FileInfo(args[2]));
  if (source == null) {
    Console.Error.WriteLine($"could not read {args[2]}");
    return 2;
  }

  // Written through the path rather than the byte array, because that is the route a caller with
  // somewhere to put the file takes, and it is the only one that reaches what belongs beside it: a
  // palette in a companion, a size in a companion, or a name the format writes into itself and then
  // refuses to open under any other. Handing a tool the byte array's output instead measures a file
  // nobody would ever have written.
  if (!FormatRegistry.Write(source, wanted.Format, new FileInfo(args[3]))) {
    Console.Error.WriteLine($"{wanted.Name} would not write {args[3]}");
    return 2;
  }

  Console.WriteLine($"{wanted.Name}: {new FileInfo(args[3]).Length} bytes from {source.Width}x{source.Height}");
  return 0;
}

// Every frame of a multi-frame file, rather than only the one the ordinary decode writes.
//
// The bulk decode below writes a single picture per sample, which is the right measurement for the
// several hundred formats holding one. It is not a measurement at all for the two dozen holding a
// sequence: a container whose frames are all decoded from the first chunk, or whose frame list is
// off by one, writes exactly the same first frame as a correct one and passes. This writes them all,
// numbered, so an oracle's own per-frame extraction can be compared against ours frame by frame.
//
//   Decode --frames <file> <output directory>
if (args[0] == "--frames") {
  if (args.Length < 3) {
    Console.Error.WriteLine("usage: Decode --frames <file> <output directory>");
    return 2;
  }

  var source = new FileInfo(args[1]);
  var destination = args[2];
  Directory.CreateDirectory(destination);

  var format = FormatRegistry.AllFormats.FirstOrDefault(entry
    => entry.GetImageCount != null
       && entry.AllExtensions?.Any(e => string.Equals(e, source.Extension, StringComparison.OrdinalIgnoreCase)) == true);

  if (format == null) {
    Console.Error.WriteLine($"no multi-frame format claims {source.Extension}");
    return 2;
  }

  // Through the throwing entry point: a container this cannot read has to say so here rather than
  // report zero frames, which is what the silent one would do and is indistinguishable from a file
  // that really holds none.
  format.LoadRawImageOrThrow?.Invoke(source);

  var frameCount = format.GetImageCount!(source);
  var stem = Path.GetFileNameWithoutExtension(source.Name);
  for (var index = 0; index < frameCount; ++index) {
    var frame = format.LoadRawImageAtIndex?.Invoke(source, index);
    if (frame == null) {
      Console.Error.WriteLine($"{format.Name}: frame {index} of {frameCount} did not decode");
      return 1;
    }

    var rgb = frame.ToRgb24();
    var wanted = (long)frame.Width * frame.Height * 3;
    if (rgb.LongLength < wanted) {
      Console.Error.WriteLine($"{format.Name}: frame {index} produced {rgb.LongLength} bytes where {wanted} were needed");
      return 1;
    }

    using var file = File.Create(Path.Combine(destination, $"{stem}_{index:00}.ppm"));
    file.Write(Encoding.ASCII.GetBytes($"P6\n{frame.Width} {frame.Height}\n255\n"));
    file.Write(rgb, 0, (int)wanted);
  }

  Console.WriteLine($"{format.Name}: {frameCount} frame(s) from {source.Name}");
  return 0;
}

if (args.Length < 2) {
  Console.Error.WriteLine("usage: Decode <sample directory> <output directory>");
  return 2;
}

// Reading a file is not the same as reading it correctly. A reader that takes its size from the
// wrong offset still reports success, and the size it invents can be enormous — one sample of 6998
// bytes was read as three and a third billion pixels. Nothing downstream questions that, so a viewer
// asked to open it tries to allocate for it. This lists the decodes whose stated size cannot be
// squared with the file it came from.
if (args[0] == "--implausible") {
  var suspect = 0;
  foreach (var path in Directory.GetFiles(args[1]).OrderBy(x => x, StringComparer.Ordinal)) {
    var candidate = new FileInfo(path);
    RawImage? decoded;
    try {
      decoded = FormatRegistry.Read(candidate);
    } catch (Exception) {
      continue;
    }

    if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0)
      continue;

    // No format here draws a picture wider or taller than this, and none of the real ones come close.
    if (decoded.Width <= 20000 && decoded.Height <= 20000)
      continue;

    ++suspect;
    Console.WriteLine($"{candidate.Name}\t{decoded.Width}x{decoded.Height}\tfrom {candidate.Length} bytes");
  }

  Console.WriteLine($"{suspect} decode(s) stated a size the file cannot hold");
  return suspect == 0 ? 0 : 1;
}

var samples = args[0];
var output = args[1];
Directory.CreateDirectory(output);

// The file is read by name rather than by bytes: several families share one layout across a set of
// extensions, and the extension is the only thing saying which variant a file is.
var written = 0;
var total = 0;
foreach (var path in Directory.GetFiles(samples).OrderBy(x => x, StringComparer.Ordinal)) {
  ++total;
  try {
    var image = FormatRegistry.Read(new FileInfo(path));
    if (image == null || image.Width <= 0 || image.Height <= 0)
      continue;

    // A picture far larger than any of these formats can hold is a misidentification, not a decode.
    if ((long)image.Width * image.Height > 40_000_000)
      continue;

    var rgb = image.ToRgb24();
    var wanted = (long)image.Width * image.Height * 3;
    if (rgb.LongLength < wanted)
      continue;

    using (var file = File.Create(Path.Combine(output, Path.GetFileName(path) + ".ppm"))) {
      file.Write(Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n"));
      file.Write(rgb, 0, (int)wanted);
    }

    ++written;
  } catch (Exception) {
    // Refusing a file is an answer, and the comparison counts it as one.
  }

  // And every other reading the registry can give of the same name.
  //
  // Fifty-odd extensions here are claimed by more than one format, and the two reference tools do
  // not always mean the same one by a name: a .sc2 is a Paintworks screen to one and an MSX Screen 2
  // to the other, and both are right about their own file. Writing only the first reading measures
  // which claimant the registry happened to order first, not whether this project can read the file.
  // The comparison takes the best of these, so a sample counts as read when any of our formats reads
  // it correctly — which is the question the report is asking.
  var alternate = 0;
  foreach (var entry in FormatRegistry.AllFormats) {
    if (entry.LoadRawImageOrThrow == null
        || entry.AllExtensions?.Any(e => string.Equals(e, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)) != true)
      continue;

    try {
      var other = entry.LoadRawImageOrThrow(new FileInfo(path));
      if (other == null || other.Width <= 0 || other.Height <= 0 || (long)other.Width * other.Height > 40_000_000)
        continue;

      var bytes = other.ToRgb24();
      var need = (long)other.Width * other.Height * 3;
      if (bytes.LongLength < need)
        continue;

      using var file = File.Create(Path.Combine(output, Path.GetFileName(path) + $".alt{++alternate}.ppm"));
      file.Write(Encoding.ASCII.GetBytes($"P6\n{other.Width} {other.Height}\n255\n"));
      file.Write(bytes, 0, (int)need);
    } catch (Exception) {
      // A format refusing a name it claims is the ordinary case here.
    }
  }
}

Console.WriteLine($"we decoded {written} of {total} samples");
return 0;
