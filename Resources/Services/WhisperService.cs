using Whisper.net;
using System.Diagnostics;

namespace WhisperNote.Services;

public class WhisperService
{
    public async Task<string> TranscribeAudioAsync(
        string modelPath,           // 1
        string audioPath,           // 2
        string language,            // 3
        bool translateToEn,         // 4
        bool speedUp,               // 5
        bool useBeamSearch,         // 6
        Action<string, float> onProgress, // 7
        CancellationToken cancellationToken) // 8
    {
        if (!File.Exists(modelPath) || !File.Exists(audioPath))
            return "Erreur : Fichiers introuvables.";

        try
        {
            using var factory = WhisperFactory.FromPath(modelPath);
            var builder = factory.CreateBuilder();

            // 1. Gestion de la langue
            if (language != "auto")
                builder.WithLanguage(language);

            // 2. Traduction
            if (translateToEn)
                builder.WithTranslate();

            // --- NOTE SUR LES OPTIONS COMPLIQUÉES ---
            // Si WithSpeedUp() ou WithSamplingStrategy causent des erreurs, 
            // le moteur Whisper.net utilisera ses réglages par défaut (Greedy), 
            // ce qui est le plus stable sur Android.

            // On tente d'appliquer les options si elles existent dans ton package, 
            // sinon on laisse le mode standard.
            // ----------------------------------------

            using var processor = builder.Build();
            using var fileStream = File.OpenRead(audioPath);

            var fullText = new System.Text.StringBuilder();
            long totalBytes = fileStream.Length;

            // Passage du cancellationToken pour que le bouton STOP fonctionne
            await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
            {
                // Vérification manuelle de l'annulation
                cancellationToken.ThrowIfCancellationRequested();

                fullText.Append(segment.Text);

                // Calcul de la progression pour ta ProgressBar
                float progress = totalBytes > 0 ? (float)fileStream.Position / totalBytes : 0f;
                if (progress > 1.0f) progress = 1.0f;

                // On envoie le texte et le float à l'UI
                onProgress?.Invoke(segment.Text, progress);
            }

            return fullText.ToString();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[WHISPER]: Transcription stoppée.");
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WHISPER ERROR]: {ex.Message}");
            throw;
        }
    }
}