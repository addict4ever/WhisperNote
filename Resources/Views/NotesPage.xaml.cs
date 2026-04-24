using System.Collections.ObjectModel;

namespace WhisperNote.Views;

public partial class NotesPage : ContentPage
{
    // Collection pour la liste des fichiers
    public ObservableCollection<FileInfo> MarkdownFiles { get; set; } = new();

    // Chemin vers le dossier prout_record_md
    private readonly string _targetPath = Path.Combine(
        Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath,
        "prout_record_md");

    public NotesPage()
    {
        InitializeComponent();
        NotesListView.ItemsSource = MarkdownFiles;
    }

    // Se lance à chaque fois que tu reviens sur l'onglet
    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadFiles();
    }

    private void LoadFiles()
    {
        try
        {
            if (!Directory.Exists(_targetPath))
            {
                Directory.CreateDirectory(_targetPath);
            }

            var files = new DirectoryInfo(_targetPath).GetFiles("*.md");

            MarkdownFiles.Clear();
            // Tri par date de modification (plus récent en haut)
            foreach (var file in files.OrderByDescending(f => f.LastWriteTime))
            {
                MarkdownFiles.Add(file);
            }
        }
        catch (Exception ex)
        {
            DisplayAlert("Erreur de lecture", ex.Message, "OK");
        }
    }

    // --- ACTIONS DE LA PAGE ---

    private void RefreshNotes(object sender, EventArgs e) => LoadFiles();

    private async void OnNewNote(object sender, EventArgs e)
    {
        // Ouvre l'éditeur en mode "nouveau"
        await Navigation.PushAsync(new NoteEditorPage());
    }

    // --- ACTIONS SUR LES FICHIERS ---

    private async void OnEditNote(object sender, EventArgs e)
    {
        // Récupère le fichier via le CommandParameter du bouton
        var button = sender as Button;
        if (button?.CommandParameter is FileInfo file)
        {
            await Navigation.PushAsync(new NoteEditorPage(file.FullName));
        }
    }

    private async void OnDeleteNote(object sender, EventArgs e)
    {
        // Fonctionne pour SwipeItem et Button
        var file = (sender as BindableObject)?.BindingContext as FileInfo;

        if (file == null) return;

        bool confirm = await DisplayAlert("🗑️ Suppression", $"Voulez-vous supprimer {file.Name} ?", "Oui", "Non");
        if (confirm)
        {
            try
            {
                file.Delete();
                LoadFiles(); // Rafraîchir la liste
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", ex.Message, "OK");
            }
        }
    }

    private async void OnCloneNote(object sender, EventArgs e)
    {
        var file = (sender as BindableObject)?.BindingContext as FileInfo;
        if (file == null) return;

        try
        {
            string newFileName = $"Copie_{DateTime.Now:mm_ss}_{file.Name}";
            string newPath = Path.Combine(_targetPath, newFileName);

            File.Copy(file.FullName, newPath);
            LoadFiles();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur clonage", ex.Message, "OK");
        }
    }

    private async void OnShareNote(object sender, EventArgs e)
    {
        var button = sender as Button;
        if (button?.CommandParameter is FileInfo file)
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Partager ma note",
                File = new ShareFile(file.FullName)
            });
        }
    }

    // On désactive OnNoteSelected car on utilise maintenant les boutons Edit/Share
    private void OnNoteSelected(object sender, SelectionChangedEventArgs e)
    {
        NotesListView.SelectedItem = null;
    }
}