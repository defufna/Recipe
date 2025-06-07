using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using System;
using System.Collections.Generic;
using System.IO;

namespace RecipeVectorSearch
{
    /// <summary>
    /// Handles tokenization and embedding generation using an ONNX model.
    /// </summary>
    internal class EmbeddingService : IDisposable
    {
        private readonly InferenceSession _onnxSession;
        private readonly BertTokenizer _tokenizer;

        public EmbeddingService(string modelPath, string tokenPath)
        {
            if (!File.Exists(modelPath) || !File.Exists(tokenPath))
            {
                throw new FileNotFoundException($"The model or tokenizer file was not found. Searched for '{modelPath}' and '{tokenPath}'.");
            }

            var options = new SessionOptions();
            // Append execution providers if desired (e.g., for GPU acceleration)
            // options.AppendExecutionProvider_OpenVINO("GPU"); 
            _onnxSession = new InferenceSession(modelPath, options);
            _tokenizer = BertTokenizer.Create(tokenPath);
        }

        public bool TryGenerateEmbedding(string[] ingredients, out DenseTensor<float>? embedding)
        {
            var inputText = string.Join(" ", ingredients);
            if (string.IsNullOrWhiteSpace(inputText))
            {
                embedding = null;
                return false;
            }
            
            DenseTensor<long> inputTensor = Tokenize(inputText);
            DenseTensor<long> attentionMask = CreateAttentionMask(inputTensor);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            };

            try
            {
                using var results = _onnxSession.Run(inputs);
                // The embedding is typically the first or second result, depending on the model.
                // It is often the "last_hidden_state" or a pooled output. Adjust the index if necessary.
                var lastHiddenState = (DenseTensor<float>)results[0].Value;
                embedding = MeanPooling(lastHiddenState, attentionMask);
                return true;
            }
            catch (OnnxRuntimeException ex)
            {
                // Log the error for debugging purposes
                File.AppendAllText("onnx_error.log", $"Failed to generate embedding for input: '{inputText}'\n{ex}\n\n");
                embedding = null;
                return false;
            }
        }

        private DenseTensor<long> Tokenize(string text)
        {
            IReadOnlyList<long> encoded = _tokenizer.EncodeToIds(text).Select(i => (long)i).ToList();
            var result = new DenseTensor<long>(new[] { 1, encoded.Count });
            for (int i = 0; i < encoded.Count; i++)
            {
                result[0, i] = encoded[i];
            }
            return result;
        }

        private static DenseTensor<long> CreateAttentionMask(DenseTensor<long> inputTensor)
        {
            var attentionMask = new DenseTensor<long>(inputTensor.Dimensions);
            for (int i = 0; i < inputTensor.Length; i++)
            {
                attentionMask[0, i] = 1; // A mask of 1s for all input tokens.
            }
            return attentionMask;
        }
        
        private static DenseTensor<float> MeanPooling(DenseTensor<float> lastHiddenState, DenseTensor<long> attentionMask)
        {
            int batchSize = lastHiddenState.Dimensions[0];
            int sequenceLength = lastHiddenState.Dimensions[1];
            int hiddenSize = lastHiddenState.Dimensions[2];
            
            var pooledOutput = new DenseTensor<float>(new[] { batchSize, hiddenSize });

            for (int i = 0; i < batchSize; i++)
            {
                float tokenCount = 0;
                for (int j = 0; j < sequenceLength; j++)
                {
                    if (attentionMask[i, j] == 1)
                    {
                        tokenCount++;
                        for (int k = 0; k < hiddenSize; k++)
                        {
                            pooledOutput[i, k] += lastHiddenState[i, j, k];
                        }
                    }
                }

                if(tokenCount > 0)
                {
                    for (int k = 0; k < hiddenSize; k++)
                    {
                        pooledOutput[i, k] /= tokenCount;
                    }
                }
            }
            return pooledOutput;
        }


        public void Dispose()
        {
            _onnxSession?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}