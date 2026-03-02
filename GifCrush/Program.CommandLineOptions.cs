using CommandLine;

namespace GifOptimizer;

public static partial class Program {
  public class CommandLineOptions {
    [Option('i', "input", Required = true, HelpText = "Input GIF file path")]
    public string InputFile { get; set; } = "";

    [Option('o', "output", Required = true, HelpText = "Output GIF file path")]
    public string OutputFile { get; set; } = "";

    [Option('s', "strategies", Default = "Original,FrequencySorted,LuminanceSorted,LzwRunAware",
      HelpText = "Palette reorder strategies to try (comma-separated)")]
    public string PaletteStrategies { get; set; } = "Original,FrequencySorted,LuminanceSorted,LzwRunAware";

    [Option("optimize-disposal", Default = true, HelpText = "Optimize frame disposal methods")]
    public bool OptimizeDisposal { get; set; } = true;

    [Option("trim-margins", Default = true, HelpText = "Trim transparent margins from frames")]
    public bool TrimMargins { get; set; } = true;

    [Option("deferred-clear", Default = true, HelpText = "Try deferred LZW clear codes")]
    public bool DeferredClear { get; set; } = true;

    [Option("frame-diff", Default = true, HelpText = "Try frame differencing for animations")]
    public bool FrameDiff { get; set; } = true;

    [Option("deduplicate", Default = true, HelpText = "Merge identical consecutive frames")]
    public bool Deduplicate { get; set; } = true;

    [Option("compression-palette", Default = false, HelpText = "Try compression-aware palette reordering (slower)")]
    public bool CompressionPalette { get; set; } = false;

    [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
    public int ParallelTasks { get; set; } = 0;

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
    public bool Verbose { get; set; } = false;
  }
}
