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
  Console.Error.WriteLine("       Decode --why <file>...");
  Console.Error.WriteLine("       Decode --anyformat <file>...");
  return 2;
}

// Every extension anything here claims, for comparing against a tool that publishes a list rather
// than being installable — which is the only way to measure one that runs as somebody's web service.
//
// Both registries, because the comparison this feeds divides what we read by what we could read, and
// leaving the containers out shrinks both halves silently: a .mov the reference tool lists and this
// one did not would count as neither a hit nor a miss. The two lists are merged rather than printed
// apart because a name claimed on both sides is still one name to whatever is being compared against.
if (args[0] == "--extensions") {
  foreach (var extension in FormatRegistry.AllFormats
             .SelectMany(entry => entry.AllExtensions ?? Array.Empty<string>())
             .Concat(VideoParity.Extensions())
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

    // And the containers, which answer a different question about the same bytes: not "what picture
    // is this" but "what streams does this hold and is there a codec for them". A file may well be
    // taken by both, and which one is right is the reader's to judge from what each says.
    VideoParity.Describe(file);
  }

  return 0;
}

if (args[0] == "--why") {
  foreach (var path in args[1..]) {
    var file = new FileInfo(path);
    var entries = FormatRegistry.AllFormats
      .Where(entry => entry.AllExtensions?.Any(e => string.Equals(e, file.Extension, StringComparison.OrdinalIgnoreCase)) == true)
      .ToArray();

    // Both registries, and both counted. A name claimed on each side is claimed twice, and picking
    // one to report would hide the contest — .avi is a container here and could perfectly well be a
    // still somewhere too, the way .mjpg already is a JPEG.
    var containers = VideoParity.ContainersClaiming(file);
    Console.WriteLine(
      $"{file.Name} ({file.Length} bytes) — {entries.Length} image format(s) and {containers.Count} container(s) claim {file.Extension}");

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

    VideoParity.Explain(file);
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

  // A container is asked as well as an image format, and asked first when no image format claims the
  // name at all. Frame-by-frame comparison was added here for AVI in the first place; when AVI moved
  // into the video package this path stopped being able to see it, and the coverage went with it.
  var containers = VideoParity.ContainersClaiming(animation);
  if (formatEntry != null && containers.Count > 0)
    Console.WriteLine($"note: {animation.Name} is claimed by the image format {formatEntry.Name} and by {containers.Count} container(s); writing the container's frames");

  if (containers.Count > 0)
    return VideoParity.WriteFrames(animation, into, containers[0]);

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
