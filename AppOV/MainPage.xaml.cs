using AppOV.Views;

namespace AppOV
{
    public partial class MainPage : ContentPage
    {
        private const string EdenDEVUrl = "https://eden.oficinavirtualdev.storey.com.ar";
        private const string EdenQAUrl = "https://eden.oficinavirtualqa.storey.com.ar";
        private const string EdesurDEVUrl = "https://edesur.oficinavirtualdev.storey.com.ar";
        private const string EdesurQAUrl = "https://edesur.oficinavirtualqa.storey.com.ar";
        private const string EdeaDEVUrl = "https://edea.oficinavirtualdev.storey.com.ar";
        private const string EdeaQAUrl = "https://edea.oficinavirtualqa.storey.com.ar";
        public MainPage()
        {
            InitializeComponent();
            //BackgroundColor = Colors.White;
            // Oculta y deshabilita la flecha de back en la barra (Shell)
            Shell.SetBackButtonBehavior(this, new BackButtonBehavior
            {
                IsVisible = false,
                IsEnabled = false
            });
            Shell.SetNavBarIsVisible(this, false);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await Navigation.PushModalAsync(new WebViewPage(EdenQAUrl), false);
        }

        // Bloquea el botón físico de atrás en Android (y el gesto de back en esta página)
        protected override bool OnBackButtonPressed()
        {
            // true = consumimos el back y NO navegamos atrás
            return true; 
        }


    }

}
