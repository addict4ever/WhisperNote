namespace WhisperNote
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // --- Enregistrement des routes pour la navigation ---
            Routing.RegisterRoute(nameof(Views.RecordsPage), typeof(Views.RecordsPage));
            Routing.RegisterRoute(nameof(Views.NotesPage), typeof(Views.NotesPage));
            Routing.RegisterRoute(nameof(Views.NoteEditorPage), typeof(Views.NoteEditorPage));
        }

        private async void OnOpenWebsiteClicked(object sender, EventArgs e)
        {
            try
            {
                // Note : J'adore le nom du site, très raccord avec le dossier prout_record_md !
                Uri uri = new("https://www.pouetpouet.ca");
                await Launcher.Default.OpenAsync(uri);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur", $"Impossible d'ouvrir le site : {ex.Message}", "OK");
            }
        }

        // Gestion du bouton "Quitter" - Version plus douce
        private void OnExitClicked(object sender, EventArgs e)
        {
#if ANDROID
            // Plus propre que KillProcess pour laisser Android fermer les ressources
            Microsoft.Maui.Controls.Application.Current?.Quit();
            // Si tu veux vraiment forcer la fermeture immédiate :
            // Android.App.Application.Context.MainExecutor.Execute(() => { Platform.CurrentActivity.FinishAndRemoveTask(); });
#elif WINDOWS
            Application.Current.Quit();
#else
            Environment.Exit(0);
#endif
        }
    }
}