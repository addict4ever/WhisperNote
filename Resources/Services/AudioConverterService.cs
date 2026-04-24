using FFMpegKit.Droid;

namespace WhisperNote.Services;

public class AudioConverterService
{
    public async Task<string> ConvertToWhisperWav(string inputPath)
    {
        // On définit le chemin de sortie dans le cache temporaire
        string outputPath = Path.Combine(FileSystem.CacheDirectory, Guid.NewGuid().ToString() + ".wav");

        if (File.Exists(outputPath)) File.Delete(outputPath);

        // Commande : 16kHz, Mono, PCM 16-bit pour Whisper
        string ffmpegCommand = $"-i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"";

        return await Task.Run(() =>
        {
            // Exécution de la commande
            var session = FFmpegKit.Execute(ffmpegCommand);

            // On accède directement aux propriétés (sans le "Get")
            var returnCode = session.ReturnCode;

            if (ReturnCode.IsSuccess(returnCode))
            {
                return outputPath;
            }
            else
            {
                // On récupère la trace d'erreur et les logs si ça échoue
                string failStackTrace = session.FailStackTrace;
                throw new Exception($"Échec FFmpeg : {failStackTrace}");
            }
        });
    }
}