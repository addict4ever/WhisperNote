using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace WhisperNote.Views
{
    public partial class AboutPage : ContentPage
    {
        private readonly List<View> particles = new();
        private bool _isAnimating = false;

        public AboutPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _isAnimating = true;

            // Initialisation des états pour l'animation
            HeaderLabel.Opacity = 0;
            HeaderLabel.TranslationY = -40;
            MainCard.Opacity = 0;
            MainCard.Scale = 0.7;

            // On ne crée les particules qu'une seule fois
            if (particles.Count == 0)
                CreateParticles();

            await Task.Delay(200);

            // Animation du titre
            _ = HeaderLabel.FadeTo(1, 800, Easing.CubicOut);
            await HeaderLabel.TranslateTo(0, 0, 800, Easing.CubicOut);

            // Animation de la carte
            _ = MainCard.FadeTo(1, 600);
            await MainCard.ScaleTo(1.0, 700, Easing.SpringOut);

            AnimateParticles();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isAnimating = false; // Arrête l'animation quand on change de page
        }

        private void CreateParticles()
        {
            var rand = new Random();
            for (int i = 0; i < 25; i++)
            {
                var particle = new Ellipse
                {
                    WidthRequest = rand.Next(4, 10),
                    HeightRequest = rand.Next(4, 10),
                    Fill = new SolidColorBrush(Color.FromArgb("#82AAFF")),
                    Opacity = rand.NextDouble()
                };

                AbsoluteLayout.SetLayoutBounds(particle, new Rect(rand.NextDouble(), rand.NextDouble(), -1, -1));
                AbsoluteLayout.SetLayoutFlags(particle, AbsoluteLayoutFlags.PositionProportional);

                ParticlesLayer.Children.Add(particle);
                particles.Add(particle);
            }
        }

        private async void AnimateParticles()
        {
            var rand = new Random();
            while (_isAnimating)
            {
                foreach (var p in particles)
                {
                    double newX = (rand.NextDouble() - 0.5) * 40;
                    double newY = (rand.NextDouble() - 0.5) * 40;

                    _ = p.TranslateTo(newX, newY, 3000, Easing.SinInOut);
                    _ = p.FadeTo(rand.NextDouble() * 0.5 + 0.2, 3000);
                }
                await Task.Delay(3000);
            }
        }

        private async void OnOpenWebsiteClicked(object sender, EventArgs e)
        {
            await WebButton.ScaleTo(0.9, 100);
            await WebButton.ScaleTo(1.0, 100);
            await Launcher.Default.OpenAsync("https://pouetpouet.ca");
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            Application.Current.Quit();
        }
    }
}