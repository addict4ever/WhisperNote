using FFMpegKit.Droid;

public class AudioProcessingService
{
    public async Task<string> ApplyFilters(string inputPath, bool noise, bool isolate, bool normalize)
    {
        string outputPath = Path.Combine(FileSystem.CacheDirectory, $"proc_{Guid.NewGuid()}.wav");
        List<string> filters = new List<string>();

        if (noise) filters.Add("afftdn");
        if (isolate) filters.Add("highpass=f=150");
        if (normalize) filters.Add("loudnorm=I=-16:TP=-1.5:LRA=11");

        if (filters.Count == 0) return inputPath;

        string filterString = string.Join(",", filters);

        // FIX : On définit le format d'entrée RAW pour éviter que FFmpeg ne cherche dans le vide
        string command = $"-y -f s16le -ar 16000 -ac 1 -i \"{inputPath}\" -af \"{filterString}\" -vn \"{outputPath}\"";

        return await Task.Run(() =>
        {
            try
            {
                var session = FFmpegKit.Execute(command);
                if (ReturnCode.IsSuccess(session.ReturnCode))
                {
                    return outputPath;
                }
            }
            catch { }
            return inputPath;
        });
    }

    public async Task<string> ApplyVAD(string inputPath)
    {
        string outputPath = Path.Combine(FileSystem.CacheDirectory, $"vad_{Guid.NewGuid()}.wav");

        // FIX : Même chose ici pour le VAD
        string command = $"-y -f s16le -ar 16000 -ac 1 -i \"{inputPath}\" -af silenceremove=stop_periods=-1:stop_duration=0.5:stop_threshold=-30dB \"{outputPath}\"";

        return await Task.Run(() =>
        {
            var session = FFmpegKit.Execute(command);
            if (ReturnCode.IsSuccess(session.ReturnCode)) return outputPath;
            return inputPath;
        });
    }
}