using System.Text.RegularExpressions;
using Markdig; // N'oublie pas l'using en haut du fichier

namespace WhisperNote.Views;

public partial class NoteEditorPage : ContentPage
{
    private string _existingFilePath;

    private string _lastDeletedText = string.Empty; // Variable à ajouter en haut de ta classe

    private CancellationTokenSource _ttsCts;

    private readonly string _directoryPathMd = Path.Combine(
        Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath,
        "prout_record_md");

    private readonly string _directoryPathHtml = Path.Combine(
        Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath,
        "prout_record_html");

    public NoteEditorPage(string filePath = null)
    {
        InitializeComponent();
        _existingFilePath = filePath;

        if (!string.IsNullOrEmpty(_existingFilePath))
            LoadNote();
        else
            FileNameEntry.Text = $"Note_{DateTime.Now:yyyyMMdd_HHmm}";
    }

    private void LoadNote()
    {
        try
        {
            if (File.Exists(_existingFilePath))
            {
                FileNameEntry.Text = Path.GetFileNameWithoutExtension(_existingFilePath);
                NoteContentEditor.Text = File.ReadAllText(_existingFilePath);
                ShowNotification("Note chargée");
            }
        }
        catch (Exception ex) { ShowNotification("Erreur de lecture", true); }
    }

    private void OnInsertMd(object sender, EventArgs e)
    {
        if (sender is Button btn) InsertAtCursor(btn.CommandParameter.ToString());
    }

    private void OnInsertTimestamp(object sender, EventArgs e)
    {
        InsertAtCursor($"\n> 🕒 {DateTime.Now:dd/MM/yyyy HH:mm}\n");
    }

    private void InsertAtCursor(string textToInsert)
    {
        int cursor = NoteContentEditor.CursorPosition;
        string currentText = NoteContentEditor.Text ?? "";
        NoteContentEditor.Text = currentText.Insert(cursor, textToInsert);
        NoteContentEditor.CursorPosition = cursor + textToInsert.Length;
        NoteContentEditor.Focus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var count = Regex.Matches(e.NewTextValue ?? "", @"[\w']+").Count;
        WordCountLabel.Text = $"{count} mots";
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FileNameEntry.Text)) { ShowNotification("Titre requis !", true); return; }

        try
        {
            if (!Directory.Exists(_directoryPathMd)) Directory.CreateDirectory(_directoryPathMd);
            string fullPath = Path.Combine(_directoryPathMd, FileNameEntry.Text.Trim() + ".md");
            await File.WriteAllTextAsync(fullPath, NoteContentEditor.Text ?? "");

            ShowNotification("Sauvegardé ✓");
            await Task.Delay(1000);
            await Navigation.PopAsync();
        }
        catch (Exception ex) { ShowNotification("Erreur sauvegarde", true); }
    }

    private async void OnExportHtml(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NoteContentEditor.Text))
            {
                ShowNotification("La note est vide !", true);
                return;
            }

            if (!Directory.Exists(_directoryPathHtml))
                Directory.CreateDirectory(_directoryPathHtml);

            // 1. CONFIGURATION DU PIPELINE AVEC LES 10 OPTIONS
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()      // 1. Tableaux, 2. Citations, 3. Listes de définitions
                .UseAutoIdentifiers()         // 4. IDs auto pour les titres (requis pour le sommaire/TOC)
                .UseAutoLinks()               // 5. Transforme les URL en liens cliquables
                .UseEmojiAndSmiley()          // 6. Support des émojis (:rocket:, :warning:)
                .UseTaskLists()               // 7. Cases à cocher interactives visuelles
                .UseGenericAttributes()       // 8. Coloration de blocs de code
                .UseYamlFrontMatter()         // 9. Support des métadonnées invisibles
                .UseFootnotes()               // 10. Notes de bas de page [^1]
                .Build();

            // 2. CONVERSION DU MARKDOWN EN HTML
            string htmlBody = Markdown.ToHtml(NoteContentEditor.Text, pipeline);

            // 3. CRÉATION DU DOCUMENT AVEC CSS ADAPTÉ AUX NOUVELLES OPTIONS
            string title = FileNameEntry.Text ?? "Sans Titre";
            string finalHtml = $@"
        <html>
        <head>
            <meta charset='utf-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <style>
                body {{ 
                    background-color: #0F111A; 
                    color: #E0E0E0; 
                    font-family: 'Segoe UI', Roboto, sans-serif; 
                    padding: 25px; 
                    line-height: 1.6;
                }}
                h1 {{ color: #82AAFF; border-bottom: 2px solid #2E3440; padding-bottom: 10px; }}
                h2, h3 {{ color: #C792EA; margin-top: 25px; }}
                
                /* Table des matières (TOC) */
                .table-of-contents {{ background: #161821; padding: 15px; border-radius: 8px; border: 1px solid #2E3440; margin-bottom: 20px; }}
                .table-of-contents ul {{ padding-left: 20px; }}
                
                /* Listes de tâches */
                .task-list-item {{ list-style-type: none; }}
                .task-list-item-checkbox {{ margin-right: 10px; transform: scale(1.2); }}
                
                /* Code et blocs */
                code {{ background-color: #161821; padding: 3px 6px; border-radius: 4px; font-family: 'Cascadia Code', monospace; color: #89DDFF; font-size: 0.9em; }}
                pre {{ background-color: #161821; padding: 15px; border-radius: 8px; overflow-x: auto; border: 1px solid #2E3440; }}
                
                /* Citations et Tableaux */
                blockquote {{ border-left: 4px solid #A3BE8C; margin: 20px 0; padding: 10px 20px; background: #1a1c25; color: #D8DEE9; }}
                table {{ border-collapse: collapse; width: 100%; margin: 25px 0; }}
                th, td {{ border: 1px solid #2E3440; padding: 12px; text-align: left; }}
                th {{ background-color: #1A1C2E; color: #82AAFF; }}
                
                /* Liens et divers */
                a {{ color: #82AAFF; text-decoration: none; }}
                a:hover {{ text-decoration: underline; }}
                hr {{ border: 0; border-top: 1px solid #2E3440; margin: 30px 0; }}
            </style>
        </head>
        <body>
            <h1>{title}</h1>
            {htmlBody}
        </body>
        </html>";

            // 4. GÉNÉRATION DU NOM ET SAUVEGARDE
            string fileName = $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            string fullPath = Path.Combine(_directoryPathHtml, fileName);
            await File.WriteAllTextAsync(fullPath, finalHtml);

            ShowNotification("Rendu Expert généré !");

            // 5. OUVERTURE AUTOMATIQUE
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(fullPath)
            });
        }
        catch (Exception ex)
        {
            ShowNotification("Erreur de rendu", true);
            System.Diagnostics.Debug.WriteLine($"[HTML EXPORT ERROR]: {ex.Message}");
        }
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NoteContentEditor.Text)) return;

        // On propose deux options : Partager le texte ou le fichier .md
        string action = await DisplayActionSheet("Partager comment ?", "Annuler", null, "Texte brut", "Fichier Markdown (.md)");

        if (action == "Texte brut")
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = FileNameEntry.Text,
                Text = NoteContentEditor.Text
            });
        }
        else if (action == "Fichier Markdown (.md)")
        {
            // On crée un fichier temporaire pour le partage
            string tempFile = Path.Combine(FileSystem.CacheDirectory, $"{FileNameEntry.Text}.md");
            await File.WriteAllTextAsync(tempFile, NoteContentEditor.Text);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Partager la note",
                File = new ShareFile(tempFile)
            });
        }
    }

    private async void OnClearClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(NoteContentEditor.Text)) return;

        bool confirm = await DisplayAlert("Purger", "Effacer tout le contenu ?", "OUI", "NON");

        if (confirm)
        {
            // On garde une copie de secours avant d'effacer
            _lastDeletedText = NoteContentEditor.Text;
            NoteContentEditor.Text = string.Empty;

            // Notification spéciale avec option "Annuler"
            ShowNotification("Note vidée. Appuie long pour restaurer (concept).");

            // Option simple : On propose directement de restaurer si erreur
            bool undo = await DisplayAlert("Oups ?", "Voulez-vous restaurer le texte effacé ?", "RESTAURER", "OK");
            if (undo)
            {
                NoteContentEditor.Text = _lastDeletedText;
                ShowNotification("Texte restauré ✓");
            }
        }
    }

    private async void OnCopyClicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NoteContentEditor.Text))
        {
            await Clipboard.Default.SetTextAsync(NoteContentEditor.Text);
            ShowNotification("Copié dans le presse-papier !");
        }
    }

    private async void OnReadAloudClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NoteContentEditor.Text)) return;

        try
        {
            // On annule la lecture précédente si elle existe
            _ttsCts?.Cancel();
            _ttsCts = new CancellationTokenSource();

            var locales = await TextToSpeech.Default.GetLocalesAsync();
            var locale = locales.FirstOrDefault(l => l.Language.StartsWith("fr", StringComparison.OrdinalIgnoreCase));

            var options = new SpeechOptions { Locale = locale, Pitch = 1.0f, Volume = 0.75f };

            // On passe le jeton (.Token) à la méthode SpeakAsync
            await TextToSpeech.Default.SpeakAsync(NoteContentEditor.Text, options, _ttsCts.Token);
        }
        catch (OperationCanceledException)
        {
            // C'est normal, l'utilisateur a cliqué sur Stop
        }
        catch (Exception ex)
        {
            ShowNotification("Erreur de lecture", true);
        }
    }

    // Pour le bouton 📋


    // Pour le bouton 🔇
    private void OnStopReadingClicked(object sender, EventArgs e)
    {
        if (_ttsCts != null)
        {
            _ttsCts.Cancel(); // Cela arrête immédiatement le moteur TTS
            _ttsCts = null;
        }

        ShowNotification("Lecture arrêtée");
    }


    // --- SYSTÈME DE NOTIFICATION MAISON (ANIMÉ) ---
    private async void ShowNotification(string message, bool isError = false)
    {
        ToastLabel.Text = message;
        CustomToast.BackgroundColor = isError ? Color.FromArgb("#BF616A") : Color.FromArgb("#2E3440");

        // Animation d'apparition (Fade In)
        await CustomToast.FadeTo(1, 250);
        await Task.Delay(2000); // Reste visible 2 secondes
        // Animation de disparition (Fade Out)
        await CustomToast.FadeTo(0, 500);
    }
}