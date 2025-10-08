// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text;
using HNSW;


HNSWCollection hnsw = new HNSWCollection(4);
hnsw = new HNSWCollection(32, 64, 40);
const int count = 10000;
const int dim = 128;

Random random = new Random();
Stopwatch stopwatch = Stopwatch.StartNew();
for (int i = 0; i < count; i++)
{
    float[] vector = RandomVector(random);
    hnsw.Add(vector);
}

Console.WriteLine($"HNSW Constructed in {stopwatch.ElapsedMilliseconds}");
hnsw.Verify();

float[] q = RandomVector(random);

List<float[]> results = hnsw.Search(q, 10, 16);
List<float[]> exactResults = hnsw.SearchExact(q, 10);


float recall = CalculateRecall(results, exactResults);

foreach (var r in results)
{
    float distance = DistanceCalculator.CosineDistance(q, r);
    Console.WriteLine($"Result: {string.Join(", ", distance)}");
}

foreach (var r in exactResults)
{
    float distance = DistanceCalculator.CosineDistance(q, r);
    Console.WriteLine($"Exact: {string.Join(", ", distance)}");
}

Console.WriteLine($"Recall: {recall * 100}%");

int correct = 0;
foreach (var node in hnsw.Nodes[0])
{
    List<float[]> result = hnsw.Search(node.Vector, 1, 10);
    if (result.Count > 0 && result[0] == node.Vector)
        correct++;
}

Console.WriteLine($"Total Recall {(float)correct/hnsw.Count*100}%");

static float[] RandomVector(Random random)
{
    float[] vector = new float[dim];
    for (int j = 0; j < vector.Length; j++)
    {
        vector[j] = random.NextSingle();
    }

    return vector;
}

static float CalculateRecall(List<float[]> results, List<float[]> exactResults)
{
    int recallCount = 0;

    int i = 0;
    int j = 0;

    while (i < results.Count && j < exactResults.Count)
    {
        if (results[i] == exactResults[j])
        {
            recallCount++;
            i++;
            j++;
        }
        else
        {
            j++;
        }
    }

    return (float)recallCount / exactResults.Count;
}




// Generate HTML with embedded SVG for each level
// StringBuilder html = new StringBuilder();
// html.AppendLine("<html><body>");

// for (int i = 1; i < hnsw.Nodes.Count; i++)
// {
//     string dot = hnsw.ToDot(i);

//     using (var process = new System.Diagnostics.Process())
//     {
//         process.StartInfo.FileName = "C:\\Program Files\\Graphviz\\bin\\dot.exe";
//         process.StartInfo.Arguments = "-Tsvg";
//         process.StartInfo.RedirectStandardInput = true;
//         process.StartInfo.RedirectStandardOutput = true;
//         process.StartInfo.UseShellExecute = false;
//         process.Start();

//         using (var writer = new System.IO.StreamWriter(process.StandardInput.BaseStream))
//         {
//             writer.Write(dot);
//         }

//         string svgOutput = process.StandardOutput.ReadToEnd();
//         process.WaitForExit();

//         html.AppendLine($"<h2>Level {i}</h2>");
//         html.AppendLine("<div style='border: 1px solid black; padding: 10px;'>");
//         html.AppendLine(svgOutput);
//         html.AppendLine("</div>");
//     }
// }

// html.AppendLine("</body></html>");
// File.WriteAllText($"HNSW.html", html.ToString());
