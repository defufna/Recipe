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
            string[] mockIngridients = ["carrot", "potato", "onion", "garlic", "tomato", "chicken", "beef", "pork", "fish", "rice", "pasta", "bread",
                                        "cheese", "egg", "milk", "yogurt", "spinach", "broccoli", "cabbage", "lettuce", "pepper", "cucumber", "zucchini",
                                        "eggplant", "mushroom", "apple", "banana", "orange", "grape", "strawberry", "blueberry", "kiwi", "peach", "pear", 
                                        "plum", "watermelon", "lemon", "lime"];

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
                    Random random = new Random();
                    string[] shuffledIngridients = mockIngridients.Clone() as string[];
                    string[] pickedIngredients = new string[10];

                    while (!cts.Token.IsCancellationRequested)
                    {
                        ShuffleAndPick(random, shuffledIngridients, pickedIngredients);

                        if (embeddingService.TryGenerateEmbedding(pickedIngredients, out DenseTensor<float> embedding))
                        {
                            localCount++;
                        }

                        if ((localCount & 64) == 0)
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

        private static void ShuffleAndPick(Random random, string[] source, string[] target)
        {
            for (int i = 0; i < target.Length; i++)
            {
                int index = random.Next(i, source.Length);
                target[i] = source[index];
                string temp = source[index];
                source[index] = source[i];
                source[i] = temp;
            }
        }
    }


}