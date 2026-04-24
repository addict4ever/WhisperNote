using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text.Json;

namespace WhisperNote.Services;

public class TranslationService
{
    private InferenceSession _encoderSession;
    private InferenceSession _decoderSession;

    private Dictionary<string, int> _vocab;
    private Dictionary<int, string> _invVocab;

    private bool _isInitialized = false;

    private const string EncoderFile = "encoder_model_int8.onnx";
    private const string DecoderFile = "decoder_model_int8.onnx";
    private const string VocabFile = "vocab.json";

    public Action<string> OnLog { get; set; }

    private void Log(string msg)
    {
        OnLog?.Invoke(msg);
        Debug.WriteLine(msg);
    }

    // ---------------- INIT ----------------
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            Log("🔄 Init FR → RU...");

            string encoderPath = await CopyResourceToLocal(EncoderFile);
            string decoderPath = await CopyResourceToLocal(DecoderFile);
            string vocabPath = await CopyResourceToLocal(VocabFile);

            _encoderSession = new InferenceSession(encoderPath);
            _decoderSession = new InferenceSession(decoderPath);

            LoadVocab(vocabPath);

            _isInitialized = true;

            Log("✅ Ready");

            var test = await TranslateText("Bonjour");
            Log("🧪 Test → " + test);
        }
        catch (Exception ex)
        {
            Log("❌ Init error: " + ex.Message);
            throw;
        }
    }

    // ---------------- TRANSLATE ----------------
    public async Task<string> TranslateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (!_isInitialized) await InitializeAsync();

        return await Task.Run(() =>
        {
            try
            {
                // 🔹 ENCODE SIMPLE
                var inputIds = Encode(text);
                inputIds.Add(2); // EOS

                long[] input = inputIds.Select(x => (long)x).ToArray();
                long[] mask = input.Select(_ => 1L).ToArray();

                // 🔹 ENCODER
                var encoderInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids",
                        new DenseTensor<long>(input, new[] { 1, input.Length })),

                    NamedOnnxValue.CreateFromTensor("attention_mask",
                        new DenseTensor<long>(mask, new[] { 1, mask.Length }))
                };

                using var encoderOut = _encoderSession.Run(encoderInputs);
                var hidden = encoderOut.First().Value as DenseTensor<float>;

                // 🔹 DECODER
                List<long> outputIds = new() { 0 };

                for (int i = 0; i < 80; i++)
                {
                    var decoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids",
                            new DenseTensor<long>(outputIds.ToArray(), new[] { 1, outputIds.Count })),

                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),

                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask",
                            new DenseTensor<long>(mask, new[] { 1, mask.Length }))
                    };

                    using var decoderOut = _decoderSession.Run(decoderInputs);
                    var logits = decoderOut.First().Value as DenseTensor<float>;

                    int next = ArgMax(logits, outputIds.Count - 1);

                    if (next == 2) break;
                    if (outputIds.Count > 2 && next == outputIds.Last()) break;

                    outputIds.Add(next);
                }

                // 🔹 DECODE SIMPLE
                return Decode(outputIds.Skip(1).ToList());
            }
            catch (Exception ex)
            {
                Log("🔥 Error: " + ex.Message);
                return "[Erreur]";
            }
        });
    }

    // ---------------- TOKENIZER SIMPLE ----------------
    private void LoadVocab(string path)
    {
        var json = File.ReadAllText(path);

        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
        _invVocab = _vocab.ToDictionary(x => x.Value, x => x.Key);
    }

    private List<int> Encode(string text)
    {
        var tokens = text
            .ToLower()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        List<int> ids = new();

        foreach (var t in tokens)
        {
            if (_vocab.TryGetValue(t, out int id))
                ids.Add(id);
            else
                ids.Add(1); // UNK
        }

        return ids;
    }

    private string Decode(List<long> ids)
    {
        var words = ids
            .Where(id => _invVocab.ContainsKey((int)id))
            .Select(id => _invVocab[(int)id]);

        return string.Join(" ", words);
    }

    // ---------------- ARGMAX ----------------
    private int ArgMax(DenseTensor<float> logits, int pos)
    {
        int vocab = logits.Dimensions[2];
        float max = float.MinValue;
        int index = 0;

        for (int i = 0; i < vocab; i++)
        {
            float val = logits[0, pos, i];
            if (val > max)
            {
                max = val;
                index = i;
            }
        }

        return index;
    }

    // ---------------- FILE COPY ----------------
    private async Task<string> CopyResourceToLocal(string file)
    {
        string path = Path.Combine(FileSystem.CacheDirectory, file);

        using var stream = await FileSystem.OpenAppPackageFileAsync(file);
        using var fs = File.Create(path);
        await stream.CopyToAsync(fs);

        return path;
    }
}