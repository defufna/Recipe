using System.Diagnostics;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RecipeVectorSearch
{
    public class Benchmark
    {
        private readonly Action waitAll;
        private readonly CancellationTokenSource cts;

        private Benchmark(Action waitAll, CancellationTokenSource cts)
        {
            this.waitAll = waitAll;
            this.cts = cts;
        }

        public void Stop()
        {
            cts.Cancel();
            waitAll();
        }
        
        public static Benchmark Run(ExecutionProvider provider, Action<float> progressCallback, int parallelism = 1)
        {
            File.AppendAllText("debug.txt", "Starting benchmark with parallelism: " + parallelism + Environment.NewLine);
            string[] mockIngridients = ["carrot", "potato", "onion"];

            EmbeddingService embeddingService = new EmbeddingService("minilm_onnx/model.onnx", "minilm_onnx/vocab.txt", provider);
            CancellationTokenSource cts = new CancellationTokenSource();

            long count = 0;

            Thread updater = new Thread(() =>
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (!cts.Token.IsCancellationRequested)
                {
                    long currentCount = Interlocked.Exchange(ref count, 0);
                    progressCallback?.Invoke(currentCount / (float)stopwatch.Elapsed.TotalSeconds);
                    stopwatch.Restart();
                    Thread.Sleep(1000);
                }
            });

            Thread[] threads = new Thread[parallelism];

            for (int i = 0; i < parallelism; i++)
            {
                threads[i] = new Thread(() =>
                {
                    long localCount = 0;
                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (embeddingService.TryGenerateEmbedding(mockIngridients, out DenseTensor<float> embedding))
                        {
                            localCount++;
                        }
                        
                        if((localCount & 64) == 0)
                        {
                            Interlocked.Add(ref count, localCount);
                            localCount = 0;
                        }
                    }
                });
                threads[i].Start();
            }
            updater.Start();

            void WaitAll()
            {
                updater.Join();
                foreach (var thread in threads)
                {
                    thread.Join();
                }
            }

            return new Benchmark(WaitAll, cts);
        }
    }


}