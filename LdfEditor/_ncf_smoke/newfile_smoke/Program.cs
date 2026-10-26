using LdfEditor.Services;
foreach (var (label, key) in LdfDefaultModelCreator.Dialects) {
  var db = LdfDefaultModelCreator.Create(key);
  var text = LdfWriter.Write(db);
  Console.WriteLine("=== " + label + " [" + key + "] ===");
  Console.WriteLine(string.Join("\n", text.Split('\n').Take(20)));
  Console.WriteLine();
}
