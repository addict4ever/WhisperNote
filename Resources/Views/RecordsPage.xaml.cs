using System.Collections.ObjectModel;
using System.IO;

namespace WhisperNote.Views;

public partial class RecordsPage : ContentPage
{
    public class RecordFile
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public DateTime CreationDate { get; set; }
        public string SizeLabel { get; set; }
    }

    public ObservableCollection<RecordFile> Records { get; set; } = new();

    public RecordsPage()
    {
        InitializeComponent();
        RecordsListView.ItemsSource = Records;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadRecords();
    }

    private void LoadRecords()
    {
        try
        {
            Records.Clear();
            string folderPath = "";

#if ANDROID
            // Récupère le chemin : /storage/emulated/0/Documents/Prout_records
            var publicDocs = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath;
            folderPath = Path.Combine(publicDocs, "Prout_records");
#else
        // Fallback pour les tests sur Windows ou iOS
        folderPath = Path.Combine(FileSystem.AppDataDirectory, "Prout_records");
#endif

            if (Directory.Exists(folderPath))
            {
                // On filtre les .wav et on les trie par date (plus récent en premier)
                var files = Directory.GetFiles(folderPath, "*.wav")
                                     .Select(f => new FileInfo(f))
                                     .OrderByDescending(f => f.CreationTime);

                foreach (var file in files)
                {
                    Records.Add(new RecordFile
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        CreationDate = file.CreationTime,
                        // Calcul de la taille en MB pour l'affichage
                        SizeLabel = $"{(file.Length / 1024.0 / 1024.0):F2} MB"
                    });
                }
            }
            else
            {
                // Optionnel : Créer le dossier s'il n'existe pas encore
                Directory.CreateDirectory(folderPath);
            }
        }
        catch (Exception ex)
        {
            // En cas de problème de permissions Android
            MainThread.BeginInvokeOnMainThread(async () => {
                await DisplayAlert("Erreur de lecture", "Impossible d'accéder au stockage : " + ex.Message, "OK");
            });
        }
    }

    // --- ACTIONS ---

    private async void OnPlayRecord(object sender, EventArgs e)
    {
        var record = (RecordFile)((Button)sender).CommandParameter;
        try
        {
            // Ouvre le lecteur audio par défaut du téléphone
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(record.FullPath)
            });
        }
        catch (Exception ex) { await DisplayAlert("Erreur", "Impossible de lire le fichier", "OK"); }
    }

    private async void OnShareRecord(object sender, EventArgs e)
    {
        var record = (RecordFile)((Button)sender).CommandParameter;
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Partager l'enregistrement",
            File = new ShareFile(record.FullPath)
        });
    }

    private async void OnTranscribeRecord(object sender, EventArgs e)
    {
        var record = (RecordFile)((Button)sender).CommandParameter;

        // Logique pour renvoyer vers la page principale et lancer la transcription
        // On peut passer le chemin du fichier via un message ou un paramètre
        bool go = await DisplayAlert("Transcription", "Envoyer ce fichier vers l'IA ?", "Oui", "Non");
        if (go)
        {
            // Ici, tu peux utiliser MessagingCenter ou simplement stocker le chemin 
            // dans une variable statique pour que MainPage le récupère.
            Preferences.Set("PendingFile", record.FullPath);
            await Shell.Current.GoToAsync("///MainPage");
        }
    }

    private async void OnDeleteRecord(object sender, EventArgs e)
    {
        var record = (RecordFile)((Button)sender).CommandParameter;
        bool confirm = await DisplayAlert("Supprimer", "Effacer définitivement ?", "Oui", "Annuler");

        if (confirm && File.Exists(record.FullPath))
        {
            File.Delete(record.FullPath);
            Records.Remove(record);
        }
    }
}