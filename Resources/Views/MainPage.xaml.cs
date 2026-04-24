using WhisperNote.Services;
using System.Diagnostics;
using Microsoft.Maui.Devices;
using Android.Media.Audiofx;
using Microsoft.ML.OnnxRuntime.Tensors;

using Microsoft.ML.OnnxRuntime;




#if ANDROID
using Android.Media;
#endif

namespace WhisperNote.Views;

public partial class MainPage : ContentPage
{
    private string _selectedModelPath = "";
    private readonly WhisperService _whisperService = new();

    // Timer pour la limite de 1 minute
    private System.Timers.Timer _recordTimer;
    private readonly TimeSpan _maxDuration = TimeSpan.FromMinutes(1);
    private CancellationTokenSource _cts;
    private CancellationTokenSource _ocrCts;
    private string _pendingAudioPath = ""; // Stocke le fichier prêt à être transcrit

    private readonly int _maxFileDurationMinutes = 20; // Limite à 20 minutes par exemple
    private readonly AudioProcessingService _audioProcessingService = new(); // Nouveau service
    private readonly TranslationService _translationService = new TranslationService();

#if ANDROID
    private AudioRecord _audioRecord;
    private bool _isRecording = false;
    private string _tempRawPath;
#endif

    public MainPage()
    {
        InitializeComponent();
        SetupTimer();
        _translationService.OnLog = (msg) => LogMessage(msg);
        Task.Run(async () => await LoadDefaultModelAsync());
    }

    private async Task LoadDefaultModelAsync()
    {
        try
        {
            // Nom du fichier dans Resources/Raw
            string fileName = "ggml-small.bin";
            string targetPath = Path.Combine(FileSystem.CacheDirectory, fileName);

            // On ne le copie que s'il n'existe pas déjà dans le cache pour gagner du temps
            if (!File.Exists(targetPath))
            {
                MainThread.BeginInvokeOnMainThread(() => LogMessage("📦 Extraction du modèle par défaut..."));

                using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
                using var fileStream = File.Create(targetPath);
                await stream.CopyToAsync(fileStream);
            }

            _selectedModelPath = targetPath;

            MainThread.BeginInvokeOnMainThread(() => {
                LogMessage("✅ Modèle chargé (Ressources locales)");
                UpdateReadyStatus();
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                LogMessage($"⚠️ Échec du chargement auto : {ex.Message}"));
        }
    }
    private async void OnLogDoubleTapped(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogLabel.Text))
            return;

        try
        {
            // 1. Copier tout l'historique des logs
            await Clipboard.Default.SetTextAsync(LogLabel.Text);

            // 2. Vibration (feedback haptique)
            try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { }

            // 3. Feedback visuel (le texte flashe en blanc)
            var originalColor = LogLabel.TextColor;
            LogLabel.TextColor = Colors.White;

            // On logue l'action (elle apparaîtra dans le presse-papier aussi si on est rapide !)
            LogMessage("📋 Historique des logs copié.");

            await Task.Delay(150);
            LogLabel.TextColor = originalColor;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur de copie logs : {ex.Message}");
        }
    }

    private async void OnHeaderDoubleTapped(object sender, EventArgs e)
    {
        if (sender is Label label)
        {
            try
            {
                // 1. Copier le texte dans le presse-papier
                await Clipboard.Default.SetTextAsync(label.Text);

                // 2. Petit feedback haptique (vibration)
                try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { }

                // 3. Effet visuel : le texte flashe en blanc
                var originalColor = label.TextColor;
                label.TextColor = Colors.White;

                // 4. Log l'action
                LogMessage("📋 Titre copié dans le presse-papier !");

                await Task.Delay(150);
                label.TextColor = originalColor;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Erreur de copie : {ex.Message}");
            }
        }
    }

    private async void OnTranscriptionDoubleTapped(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TranscriptionEditor.Text))
            return;

        try
        {
            // Copie dans le presse-papier
            await Clipboard.Default.SetTextAsync(TranscriptionEditor.Text);

            // Optionnel : Vibration pour confirmer (sur mobile)
            try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { }

            // Affichage d'une notification dans tes logs existants
            LogMessage("📋 Texte copié dans le presse-papier !");

            // Petit feedback visuel rapide
            await DisplayAlert("Copié", "Le texte a été copié avec succès.", "OK");
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur de copie : {ex.Message}");
        }
    }

    private void SetupTimer()
    {
        _recordTimer = new System.Timers.Timer(_maxDuration.TotalMilliseconds);
        _recordTimer.AutoReset = false;
        _recordTimer.Elapsed += async (s, e) =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                LogMessage("🕒 Limite de 1 min atteinte. Arrêt...");
                await StopRecordingAndProcess();
            });
        };
    }

    private void LogMessage(string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogLabel.Text += $"[{timestamp}] {message}{Environment.NewLine}";
            await Task.Delay(50);
            await LogScrollView.ScrollToAsync(LogLabel, ScrollToPosition.End, true);
        });
    }

    // --- ENREGISTREMENT ---
    private async void OnRecordAudio(object sender, EventArgs e)
    {
        // VERIFICATION DU MODELE AVANT TOUT
        if (string.IsNullOrEmpty(_selectedModelPath))
        {
            LogMessage("⚠️ Erreur : Aucun modèle Whisper (.bin) n'est chargé !");
            await DisplayAlert("Modèle Manquant", "Veuillez charger un modèle Whisper avant d'enregistrer.", "OK");

            await LoadModelButton.ScaleTo(1.2, 100);
            await LoadModelButton.ScaleTo(1.0, 100);
            return;
        }

#if ANDROID
        try
        {
            if (!_isRecording)
            {
                var status = await Permissions.RequestAsync<Permissions.Microphone>();
                if (status != PermissionStatus.Granted) return;

                // Vibration tactile pour confirmer le début
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(50));

                int sampleRate = 16000;
                var channelConfig = ChannelIn.Mono;
                var audioFormat = Android.Media.Encoding.Pcm16bit;
                int bufferSize = AudioRecord.GetMinBufferSize(sampleRate, channelConfig, audioFormat);

                _audioRecord = new AudioRecord(AudioSource.Mic, sampleRate, channelConfig, audioFormat, bufferSize);

                // --- 1. RÉDUCTION DE BRUIT MATÉRIELLE ---
                if (NoiseSuppressor.IsAvailable) // Propriété, pas de ()
                {
                    var ns = NoiseSuppressor.Create(_audioRecord.AudioSessionId);
                    if (ns != null)
                    {
                        ns.SetEnabled(true);
                        LogMessage("✨ Noise Suppressor activé.");
                    }
                }

                // --- 2. CONTRÔLE DE GAIN AUTOMATIQUE ---
                if (AutomaticGainControl.IsAvailable) // Propriété, pas de ()
                {
                    var agc = AutomaticGainControl.Create(_audioRecord.AudioSessionId);
                    if (agc != null)
                    {
                        agc.SetEnabled(true);
                        LogMessage("🔊 Gain automatique activé.");
                    }
                }

                // --- 3. ANNULATION D'ÉCHO (Optionnel, bon pour les environnements résonnants) ---
                if (AcousticEchoCanceler.IsAvailable)
                {
                    var aec = AcousticEchoCanceler.Create(_audioRecord.AudioSessionId);
                    if (aec != null) aec.SetEnabled(true);
                }

                _tempRawPath = Path.Combine(FileSystem.CacheDirectory, "temp.raw");

                _isRecording = true;
                _audioRecord.StartRecording();

                RecordButton.Text = "🛑 STOP";
                RecordButton.BackgroundColor = Colors.DarkRed;
                LogMessage("🎤 Enregistrement (Filtres actifs)...");

                if (LimitToggle.IsToggled) _recordTimer.Start();

                Task.Run(() =>
                {
                    using var fs = new FileStream(_tempRawPath, FileMode.Create);
                    byte[] buffer = new byte[bufferSize];
                    while (_isRecording)
                    {
                        int read = _audioRecord.Read(buffer, 0, buffer.Length);
                        if (read > 0) fs.Write(buffer, 0, read);
                    }
                });
            }
            else
            {
                // Vibration tactile pour confirmer l'arrêt
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(50));
                await StopRecordingAndProcess();
            }
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur Micro : {ex.Message}");
        }
#endif
    }

    private async Task StopRecordingAndProcess()
    {
#if ANDROID
        if (!_isRecording) return;

        try
        {
            _isRecording = false;
            _recordTimer.Stop();

            if (_audioRecord != null)
            {
                _audioRecord.Stop();
                _audioRecord.Release();
                _audioRecord = null;
            }

            RecordButton.Text = "🎤 MICRO";
            RecordButton.BackgroundColor = Color.FromArgb("#FF5252");
            Loader.IsRunning = true;
            StatusDot.Fill = Colors.Orange;

            // 1. Génération du fichier WAV brut (16kHz Mono)
            string wavPath = Path.Combine(FileSystem.CacheDirectory, "temp_rec.wav");
            using (var rawStream = File.OpenRead(_tempRawPath))
            using (var wavStream = File.Create(wavPath))
            {
                wavStream.SetLength(rawStream.Length + 44);
                wavStream.Seek(44, SeekOrigin.Begin);
                rawStream.CopyTo(wavStream);
                WriteWavHeader(wavStream, (int)rawStream.Length, 16000, 1, 16);
            }

            // Sauvegarde initiale dans ton dossier "Prout"
            string finalPath = await SaveToProutFolder(wavPath);
            LogMessage("✅ Capture terminée.");

            // --- NOUVEAU : PRÉ-TRAITEMENT DE L'ENREGISTREMENT ---

            var processor = new AudioProcessingService();

            // A. Nettoyage des bruits, isolation et gain
            if (NoiseFilterToggle.IsToggled || VoiceIsolateToggle.IsToggled || NormalizeToggle.IsToggled)
            {
                StatusLabel.Text = "NETTOYAGE MICRO...";
                finalPath = await processor.ApplyFilters(
                    finalPath,
                    NoiseFilterToggle.IsToggled,
                    VoiceIsolateToggle.IsToggled,
                    NormalizeToggle.IsToggled);
            }

            // B. Suppression des silences (VAD)
            if (VADToggle.IsToggled)
            {
                StatusLabel.Text = "COUPE SILENCES (VAD)...";
                finalPath = await processor.ApplyVAD(finalPath);
            }

            // C. Optimisation CPU (Vérification finale du format 16kHz)
            if (OptimizeFreqToggle.IsToggled)
            {
                StatusLabel.Text = "OPTIMISATION FINALE...";
                var converter = new AudioConverterService();
                finalPath = await converter.ConvertToWhisperWav(finalPath);
            }

            // 2. Lancement de la transcription
            StatusLabel.Text = "TRANSCRIPTION...";
            await ProcessAudioTask(finalPath);
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur arrêt/traitement : {ex.Message}");
            StatusLabel.Text = "ERREUR TRAITEMENT";
            StatusDot.Fill = Colors.Red;
        }
        finally
        {
            Loader.IsRunning = false;
        }
#endif
    }

    private void WriteWavHeader(FileStream fs, int rawAudioLength, int sampleRate, short channels, short bitsPerSample)
    {
        byte[] header = new byte[44];
        int byteRate = sampleRate * channels * bitsPerSample / 8;

        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BitConverter.GetBytes(rawAudioLength + 36).CopyTo(header, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        BitConverter.GetBytes(16).CopyTo(header, 16);
        BitConverter.GetBytes((short)1).CopyTo(header, 20);
        BitConverter.GetBytes(channels).CopyTo(header, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
        BitConverter.GetBytes(byteRate).CopyTo(header, 28);
        BitConverter.GetBytes((short)(channels * bitsPerSample / 8)).CopyTo(header, 32);
        BitConverter.GetBytes(bitsPerSample).CopyTo(header, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BitConverter.GetBytes(rawAudioLength).CopyTo(header, 40);

        fs.Seek(0, SeekOrigin.Begin);
        fs.Write(header, 0, 44);
    }

    private async Task<string> SaveToProutFolder(string sourcePath)
    {
        string destination = "";

#if ANDROID
        // On récupère le chemin vers le dossier "Documents" public
        var publicDocs = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath;
        string folderPath = Path.Combine(publicDocs, "Prout_records");
#else
    // Fallback pour les autres plateformes
    string folderPath = Path.Combine(FileSystem.AppDataDirectory, "Prout_records");
#endif

        try
        {
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string fileName = $"Record_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            destination = Path.Combine(folderPath, fileName);

            if (File.Exists(sourcePath))
            {
                // On utilise Copy et non Move pour éviter des problèmes de permissions inter-volumes
                File.Copy(sourcePath, destination, true);
                LogMessage($"💾 Sauvegardé dans : Documents/Prout_records");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur de sauvegarde externe : {ex.Message}");
            // Si ça échoue (souvent dû aux permissions), on replie sur le stockage interne
            return sourcePath;
        }

        return destination;
    }
    // --- TRANSCRIPTION ET MODÈLE ---
    // Méthode pour mettre à jour l'état visuel
    private void UpdateReadyStatus()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            bool isModelLoaded = !string.IsNullOrEmpty(_selectedModelPath);

            if (isModelLoaded)
            {
                StatusLabel.Text = "PRÊT";
                StatusLabel.TextColor = Colors.LimeGreen;
                StatusDot.Fill = Colors.LimeGreen;
            }
            else
            {
                StatusLabel.Text = "MODÈLE MANQUANT";
                StatusLabel.TextColor = Colors.Orange;
                StatusDot.Fill = Colors.Orange;
            }
        });
    }

    // Bouton d'arrêt d'urgence

    public async Task<string> TranslateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "...";

        try
        {
            // On récupère la langue du picker (ex: "Russian")
            string selectedLanguage = TargetLanguagePicker.SelectedItem?.ToString() ?? "Russian";

            // CORRECTION : On passe DEUX arguments (le texte ET la langue)
            return await _translationService.TranslateText(text);
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur : {ex.Message}");
            return "Erreur de traduction";
        }
    }

    private async void OnTranslateClicked(object sender, EventArgs e)
    {
        Debug.WriteLine("🖱️ [UI] Clic sur le bouton Traduire.");

        if (string.IsNullOrWhiteSpace(TranscriptionEditor.Text))
        {
            Debug.WriteLine("⚠️ [UI] Tentative de traduction sur un texte vide.");
            return;
        }

        try
        {
            Loader.IsRunning = true;
            StatusLabel.Text = "TRADUCTION...";
            StatusDot.Fill = Colors.Purple;

            // Récupération de la langue
            string target = TargetLanguagePicker.SelectedItem?.ToString() ?? "Russian";
            Debug.WriteLine($"🎯 [UI] Langue sélectionnée dans le Picker : {target}");

            // Appel du service
            string result = await _translationService.TranslateText(TranscriptionEditor.Text);

            // Affichage
            TranscriptionEditor.Text += $"\n\n--- Traduction ({target}) ---\n{result}";
            LogMessage($"✅ Traduction réussie ({target})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ [UI] Erreur lors du clic : {ex.Message}");
            LogMessage("❌ Erreur de traduction");
        }
        finally
        {
            Loader.IsRunning = false;
            StatusLabel.Text = "Prêt";
            StatusDot.Fill = Colors.Green;
            Debug.WriteLine("🏁 [UI] Opération de traduction terminée.");
        }
    }

    // 1. Bouton pour lancer l'OCR
    
    

    private void OnEmergencyStop(object sender, EventArgs e)
    {
        _cts?.Cancel();
        LogMessage("⚠️ Arrêt d'urgence demandé...");
    }

    // Modifier OnLoadModel pour mettre à jour l'état
    private async void OnLoadModel(object sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Modèle Whisper" });
        if (result != null)
        {
            _selectedModelPath = result.FullPath;
            LogMessage($"✅ Modèle : {result.FileName}");
            UpdateReadyStatus(); // Mise à jour ici
        }
    }

    private async void OnMainStartClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_pendingAudioPath) || !File.Exists(_pendingAudioPath))
        {
            await DisplayAlert("Erreur", "Aucune source audio valide.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(_selectedModelPath))
        {
            await DisplayAlert("Modèle", "Veuillez d'abord charger un modèle Whisper.", "OK");
            return;
        }

        // On cache le bouton Start et on montre le Stop pendant le travail
        MainStartButton.IsVisible = false;
        EmergencyStopButton.IsVisible = true;

        // Petite vibration rapide (Haptic feedback)
        Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(50));

        await ProcessAudioTask(_pendingAudioPath);

        // Une fois fini, on remet l'interface à zéro
        _pendingAudioPath = "";
        FileButton.IsVisible = true;
        EmergencyStopButton.IsVisible = false;
    }


    private async void OnStartTranscription(object sender, EventArgs e)
    {
        var audioFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.Android, new[] { "audio/*" } },
        { DevicePlatform.WinUI, new[] { ".mp3", ".wav", ".m4a", ".ogg" } }
    });

        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Sélectionner un audio",
            FileTypes = audioFileType
        });

        if (result == null) return;

        try
        {
            // Initialisation de l'UI
            _pendingAudioPath = result.FullPath;
            Loader.IsRunning = true;
            StatusDot.Fill = Colors.Orange;
            StatusLabel.Text = "ANALYSE...";
            MainStartButton.IsVisible = false; // On s'assure qu'il est caché au début
            FileButton.IsVisible = true;

            // 1. Vérification de la durée (Méthode utilitaire à avoir dans ta classe)
            var duration = await GetAudioDuration(_pendingAudioPath);
            if (duration > TimeSpan.FromMinutes(_maxFileDurationMinutes))
            {
                await DisplayAlert("Fichier trop volumineux",
                    $"La durée ({duration.TotalMinutes:F1} min) dépasse la limite de {_maxFileDurationMinutes} min.", "OK");
                return;
            }

            // 2. Optimisation CPU / Conversion Whisper (16kHz Mono)
            // Obligatoire si ce n'est pas un .wav ou si l'utilisateur veut forcer l'optimisation
            if (OptimizeFreqToggle.IsToggled || !result.FileName.ToLower().EndsWith(".wav"))
            {
                StatusLabel.Text = "CONVERSION 16kHz...";
                var converter = new AudioConverterService();
                var convertedPath = await converter.ConvertToWhisperWav(_pendingAudioPath);

                if (!string.IsNullOrEmpty(convertedPath) && File.Exists(convertedPath))
                    _pendingAudioPath = convertedPath;
            }

            // On utilise une seule instance pour éviter les fuites de mémoire
            var processor = new AudioProcessingService();

            // 3. Application des filtres de nettoyage (Anti-Bruit, Iso-Voix, Normalisation)
            if (NoiseFilterToggle.IsToggled || VoiceIsolateToggle.IsToggled || NormalizeToggle.IsToggled)
            {
                StatusLabel.Text = "NETTOYAGE AUDIO...";
                var filteredPath = await processor.ApplyFilters(
                    _pendingAudioPath,
                    NoiseFilterToggle.IsToggled,
                    VoiceIsolateToggle.IsToggled,
                    NormalizeToggle.IsToggled);

                if (!string.IsNullOrEmpty(filteredPath) && File.Exists(filteredPath))
                    _pendingAudioPath = filteredPath;
            }

            // 4. Application du VAD (Suppression des silences)
            if (VADToggle.IsToggled)
            {
                StatusLabel.Text = "SUPPRESSION SILENCES...";
                var vadPath = await processor.ApplyVAD(_pendingAudioPath);

                if (!string.IsNullOrEmpty(vadPath) && File.Exists(vadPath))
                    _pendingAudioPath = vadPath;
            }

            // 5. Finalisation et affichage du bouton START
            StatusLabel.Text = "PRÊT À DÉMARRER";
            StatusDot.Fill = Colors.LimeGreen;

            // C'est ici qu'on permute les boutons
            MainStartButton.IsVisible = true;
            FileButton.IsVisible = false;

            // Préparation du message de log pour savoir ce qui a été appliqué
            List<string> applied = new List<string>();
            if (NoiseFilterToggle.IsToggled) applied.Add("Bruit");
            if (VoiceIsolateToggle.IsToggled) applied.Add("Iso");
            if (VADToggle.IsToggled) applied.Add("VAD");

            string tag = applied.Count > 0 ? $" [{string.Join("/", applied)}]" : "";
            LogMessage($"✅ Traitement fini : {result.FileName}{tag}");
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur : {ex.Message}");
            StatusLabel.Text = "ERREUR PRÉ-TRAITEMENT";
            StatusDot.Fill = Colors.Red;
            await DisplayAlert("Erreur", "Le traitement audio a échoué. Essayez sans filtres.", "OK");
        }
        finally
        {
            Loader.IsRunning = false;
        }
    }

    private async Task<TimeSpan> GetAudioDuration(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var retriever = new Android.Media.MediaMetadataRetriever();
                retriever.SetDataSource(filePath);
                var time = retriever.ExtractMetadata(Android.Media.MetadataKey.Duration);
                var timeInMillis = long.Parse(time);
                return TimeSpan.FromMilliseconds(timeInMillis);
            }
            catch
            {
                return TimeSpan.Zero; // En cas d'erreur de lecture
            }
        });
    }


    private async Task ProcessAudioTask(string audioPath)
    {
        if (string.IsNullOrEmpty(_selectedModelPath))
        {
            await DisplayAlert("Modèle manquant", "Veuillez charger un modèle Whisper avant de commencer.", "OK");
            return;
        }

        // Sécurité : Vérifier si le fichier existe vraiment (surtout après les filtres FFmpeg)
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            await DisplayAlert("Erreur fichier", "Le fichier audio est introuvable ou a échoué au nettoyage.", "OK");
            return;
        }

        _cts = new CancellationTokenSource();

        try
        {
            // 1. Mise à jour de l'interface (Mode Travail)
            Loader.IsRunning = true;

            // Verrouillage de l'UI
            FileButton.IsVisible = false;
            MainStartButton.IsVisible = false;
            RecordButton.IsEnabled = false;
            EmergencyStopButton.IsVisible = true;

            StatusLabel.Text = "TRANSCRIPTION EN COURS...";
            StatusDot.Fill = Colors.Cyan;
            TranscriptionEditor.Text = "";
            TranscriptionBar.Progress = 0;
            PercentLabel.Text = "0%";

            // 2. Lancement du moteur Whisper
            // On utilise ici le audioPath qui a été passé (celui qui a subi les filtres)
            await _whisperService.TranscribeAudioAsync(
                _selectedModelPath,
                audioPath,
                LanguagePicker.SelectedItem?.ToString() ?? "auto",
                TranslateSwitch.IsToggled,
                SpeedUpSwitch.IsToggled,
                BeamSwitch.IsToggled,
                (chunk, progress) => MainThread.BeginInvokeOnMainThread(() => {
                    TranscriptionEditor.Text += chunk;
                    TranscriptionBar.Progress = progress;
                    PercentLabel.Text = $"{(progress * 100):F0}%";

                    // Auto-scroll intelligent vers le bas
                    TranscriptionScrollView.ScrollToAsync(0, TranscriptionEditor.Height, true);
                }),
                _cts.Token
            );

            LogMessage("✅ Transcription réussie.");
            StatusLabel.Text = "TERMINÉ";
            StatusDot.Fill = Colors.LimeGreen;
        }
        catch (OperationCanceledException)
        {
            LogMessage("🛑 Transcription annulée par l'utilisateur.");
            StatusLabel.Text = "ANNULÉ";
            StatusDot.Fill = Colors.Orange;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur Whisper : {ex.Message}");
            StatusLabel.Text = "ERREUR";
            StatusDot.Fill = Colors.Red;
            await DisplayAlert("Erreur", "La transcription a échoué : " + ex.Message, "OK");
        }
        finally
        {
            // 3. Remise à zéro et nettoyage des fichiers temporaires
            Loader.IsRunning = false;
            EmergencyStopButton.IsVisible = false;

            // On réactive le bouton FICHIER pour la prochaine note
            FileButton.IsVisible = true;
            RecordButton.IsEnabled = true;
            MainStartButton.IsVisible = false;

            // Important : Si c'est un fichier temporaire (cleaned_ ou vad_), on pourrait le supprimer ici
            // pour ne pas remplir le cache du téléphone, mais on vide au moins la variable.
            _pendingAudioPath = "";

            // Si tu as une méthode qui check si le modèle est toujours là pour remettre le point vert
            UpdateReadyStatus();
        }
    }

    private void OnClearTranscription(object sender, EventArgs e)
    {
        TranscriptionEditor.Text = string.Empty;
        TranscriptionBar.Progress = 0;
        PercentLabel.Text = "0%";
        LogMessage("🧹 Zone de texte nettoyée.");
    }

    // 2. Bouton Export : Sauvegarde dans Documents/prout_record_md
    private async void OnExportToMarkdown(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TranscriptionEditor.Text))
        {
            await DisplayAlert("Export", "Rien à exporter, la transcription est vide !", "OK");
            return;
        }

        try
        {
            // Chemin vers ton dossier MD (identique à ton onglet Note)
            var publicDocs = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath;
            string folderPath = Path.Combine(publicDocs, "prout_record_md");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            // Nom de fichier horodaté
            string fileName = $"Transcription_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string fullPath = Path.Combine(folderPath, fileName);

            // Écriture du fichier
            await File.WriteAllTextAsync(fullPath, TranscriptionEditor.Text);

            LogMessage($"💾 Exporté : {fileName}");
            await DisplayAlert("Succès", $"Note exportée dans Archives/Note", "OK");
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Erreur export : {ex.Message}");
            await DisplayAlert("Erreur", "Impossible d'exporter : " + ex.Message, "OK");
        }
    }

}