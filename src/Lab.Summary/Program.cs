using System.Text;
using Lab.Summary;

// Usage: dotnet run --project src/Lab.Summary -- results/results.csv > results/summary.md
var path = args.Length > 0 ? args[0] : "results/results.csv";
var rows = ResultsCsv.Parse(File.ReadAllText(path));

// UTF-8 without a BOM and "\n" line endings on every OS, so summary.md is the same file
// whichever machine writes it.
using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
stdout.Write(SummaryReport.Render(rows));
return 0;
