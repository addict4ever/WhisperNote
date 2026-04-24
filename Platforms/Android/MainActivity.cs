using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace WhisperNote;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private bool _doubleBackToExitPressedOnce = false;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // On lance la vérification des permissions au démarrage
        CheckAndRequestRequiredPermissions();
    }

    private void CheckAndRequestRequiredPermissions()
    {
        // Liste des permissions de base
        var permissionsList = new List<string>
        {
            Android.Manifest.Permission.RecordAudio,
            Android.Manifest.Permission.ReadExternalStorage,
            Android.Manifest.Permission.WriteExternalStorage
        };

        // Ajout de la permission spécifique pour Android 13+ (API 33+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            permissionsList.Add(Android.Manifest.Permission.ReadMediaAudio);
        }

        var permissionsToRequest = new List<string>();

        foreach (var permission in permissionsList)
        {
            // Correction ici : c'est Permission.Granted (avec un 's' à la fin du namespace si on utilise Android.Content.PM.Permission)
            if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
            {
                permissionsToRequest.Add(permission);
            }
        }

        if (permissionsToRequest.Count > 0)
        {
            ActivityCompat.RequestPermissions(this, permissionsToRequest.ToArray(), 0);
        }
    }

    public override void OnBackPressed()
    {
        // Si c'est la première pression
        if (!_doubleBackToExitPressedOnce)
        {
            _doubleBackToExitPressedOnce = true;
            Toast.MakeText(this, "Appuyez encore pour quitter", ToastLength.Short)?.Show();

            // Réinitialise après 2 secondes si pas de deuxième pression
            new Handler(Looper.MainLooper!).PostDelayed(() => {
                _doubleBackToExitPressedOnce = false;
            }, 2000);
        }
        else
        {
            // Deuxième pression détectée : on pose la question finale
            ShowExitConfirmation();
        }
    }

    private void ShowExitConfirmation()
    {
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Quitter Whisper Note ?");
        builder.SetMessage("Voulez-vous vraiment fermer l'application ?");
        builder.SetCancelable(false);

        builder.SetPositiveButton("OUI", (sender, args) => {
            FinishAffinity(); // Ferme proprement toutes les activités
        });

        builder.SetNegativeButton("NON", (sender, args) => {
            _doubleBackToExitPressedOnce = false; // Reset pour le prochain coup
        });

        builder.Show();
    }

    protected override void OnPause()
    {
        base.OnPause();
        MessagingCenter.Send(this, "PauseGame"); // ⏸️ Arrête la musique quand l'appli est en arrière-plan
    }

    protected override void OnResume()
    {
        base.OnResume();
        MessagingCenter.Send(this, "ResumeGame"); // ▶️ Reprend la musique quand l'appli revient au premier plan
    }


}