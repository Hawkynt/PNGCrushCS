using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("webp", HelpText = "Optimize WebP files")]
public sealed class WebPVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input WebP file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output WebP file path")]
  public string OutputFile { get; set; } = "";

  // bool? and not bool: a plain bool option is a switch, so `Default = true` could never be
  // declined — see the note in GifVerb.cs for the mechanism.
  [Option('s', "strip-metadata", Default = true, HelpText = "Strip metadata (EXIF, ICCP, XMP)")]
  public bool? StripMetadata { get; set; }

  [Option("convert", Default = false, HelpText = "Also try other formats")]
  public bool AllowConversion { get; set; } = false;

  [Option("lossy", Default = false, HelpText = "Allow lossy compression")]
  public bool AllowLossy { get; set; } = false;

  [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
  public int ParallelTasks { get; set; } = 0;

  [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
  public bool Verbose { get; set; } = false;
}
