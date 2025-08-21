// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text;
using HNSW;


HNSWCollection hnsw = new HNSWCollection(4);
hnsw = new HNSWCollection(4, 64);
const int count = 10000;

Random random = new Random();
Stopwatch stopwatch = Stopwatch.StartNew();
for (int i = 0; i < count; i++)
{
    float[] vector = new float[128];
    for (int j = 0; j < vector.Length; j++)
    {
        vector[j] = random.NextSingle();
    }
    hnsw.Add(vector);
}

Console.WriteLine($"HNSW Constructed in {stopwatch.ElapsedMilliseconds}");
// Generate HTML with embedded SVG for each level
StringBuilder html = new StringBuilder();
html.AppendLine("<html><body>");

for (int i = 1; i < hnsw.Nodes.Count; i++)
{
    string dot = hnsw.ToDot(i);

    using (var process = new System.Diagnostics.Process())
    {
        process.StartInfo.FileName = "C:\\Program Files\\Graphviz\\bin\\dot.exe";
        process.StartInfo.Arguments = "-Tsvg";
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();

        using (var writer = new System.IO.StreamWriter(process.StandardInput.BaseStream))
        {
            writer.Write(dot);
        }

        string svgOutput = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        html.AppendLine($"<h2>Level {i}</h2>");
        html.AppendLine("<div style='border: 1px solid black; padding: 10px;'>");
        html.AppendLine(svgOutput);
        html.AppendLine("</div>");
    }
}

html.AppendLine("</body></html>");
File.WriteAllText($"HNSW.html", html.ToString());
