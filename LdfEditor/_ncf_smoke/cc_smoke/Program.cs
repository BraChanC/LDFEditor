using System.IO;
using System.Text;
using LdfEditor.Services.Diagnostics;

void Dump(string label, string text, string? path = null)
{
    var r = LdfDiagnosticEngine.Analyze(text, path);
    Console.WriteLine("==== " + label + " ====");
    Console.WriteLine(
        $"errors={r.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} " +
        $"warn={r.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} " +
        $"total={r.Diagnostics.Count} db={(r.Database != null)}");
    foreach (var d in r.Diagnostics.Take(20))
        Console.WriteLine($"  [{d.Code}] L{d.Line}:{d.Column} {d.Message}");
    if (r.Diagnostics.Count > 20)
        Console.WriteLine($"  ... +{r.Diagnostics.Count - 20} more");
    Console.WriteLine();
}

void DumpFile(string path) => Dump(Path.GetFileName(path), File.ReadAllText(path), path);

DumpFile(@"E:\download\LIN-master\examples\lin_diagnostics_2.ldf");
DumpFile(@"E:\download\LIN-master\examples\lin_diagnostics.ldf");

/* Full-width punctuation recovery: report LDF0005, still parse after normalize */
var good = File.ReadAllText(@"E:\download\LIN-master\examples\lin_diagnostics.ldf");
var fw = new StringBuilder(good);
fw.Replace("LIN_description_file;", "LIN_description_file；"); /* fullwidth ; */
fw.Replace("LIN_speed = 19.2 kbps;", "LIN_speed ＝ 19.2 kbps；");
fw.Replace("Channel_name = \"DB\";", "Channel_name ＝ ＂DB＂；");
Dump("fullwidth_punct.ldf (synthetic)", fw.ToString(), "fullwidth_punct.ldf");

/* Missing semicolon on header */
var noSemi = good.Replace("LIN_speed = 19.2 kbps;", "LIN_speed = 19.2 kbps");
Dump("missing_semicolon.ldf (synthetic)", noSemi, "missing_semicolon.ldf");
