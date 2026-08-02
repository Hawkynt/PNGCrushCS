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

    using var file = File.Create(Path.Combine(output, Path.GetFileName(path) + ".ppm"));
    file.Write(Encoding.ASCII.GetBytes($"P6\n{image.Width} {image.Height}\n255\n"));
    file.Write(rgb, 0, (int)wanted);
    ++written;
  } catch (Exception) {
    // Refusing a file is an answer, and the comparison counts it as one.
  }
}

Console.WriteLine($"we decoded {written} of {total} samples");
return 0;
