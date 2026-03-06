using CommandLine;
using Crush.Core;

namespace Crush.Bmp;

public static partial class Program {
  public class CommandLineOptions : ICrushOptions {
    [Option('i', "input", Required = true, HelpText = "Input BMP file path")]
    public string InputFile { get; set; } = "";

    [Option('o', "output", Required = true, HelpText = "Output BMP file path")]
    public string OutputFile { get; set; } = "";

    [Option('c', "compression", Default = "None,Rle8,Rle4",
      HelpText = "Compression methods to try (comma-separated)")]
    public string Compressions { get; set; } = "None,Rle8,Rle4";

    [Option('a', "auto-color-mode", Default = true, HelpText = "Automatically select best color mode")]
    public bool AutoColorMode { get; set; } = true;

    [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
    public int ParallelTasks { get; set; } = 0;

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
    public bool Verbose { get; set; } = false;
  }
}
