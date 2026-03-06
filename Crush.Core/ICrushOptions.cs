namespace Crush.Core;

/// <summary>Common CLI options shared by all crush tools.</summary>
public interface ICrushOptions {
  string InputFile { get; }
  string OutputFile { get; }
  int ParallelTasks { get; }
  bool Verbose { get; }
}
