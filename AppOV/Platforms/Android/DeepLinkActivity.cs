using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace AppOV
{
    // Activity puente que recibe el deep link y reenvía a MainActivity.
    [Activity(
        Name = "com.storey.AppOV.Edelap.DeepLinkActivity",
        Theme = "@style/Maui.SplashTheme",
        LaunchMode = LaunchMode.SingleTop,
        NoHistory = true,
        Exported = true)]

    // ===== APP LINKS QA / DEV =====
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "https", DataHost = "edelap.ovqa.storey.com.ar", DataPathPrefix = "/iniciar-sesion")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "https", DataHost = "edelap.ovqa.storey.com.ar", DataPathPrefix = "/registrar")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "https", DataHost = "edelap.ovdev.storey.com.ar", DataPathPrefix = "/iniciar-sesion")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "https", DataHost = "edelap.ovdev.storey.com.ar", DataPathPrefix = "/registrar")]

    // ===== NGROK =====
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    //              DataScheme = "https", DataHost = "hemikaryotic-sanford-unmetallically.ngrok-free.dev", DataPathPrefix = "/iniciar-sesion")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    //              DataScheme = "https", DataHost = "hemikaryotic-sanford-unmetallically.ngrok-free.dev", DataPathPrefix = "/registrar")]

    // ===== DEV LOCAL =====
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "http", DataHost = "localhost", DataPort = "52953", DataPathPrefix = "/iniciar-sesion")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "http", DataHost = "localhost", DataPort = "52953", DataPathPrefix = "/registrar")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "http", DataHost = "10.0.2.2", DataPort = "52953", DataPathPrefix = "/iniciar-sesion")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "http", DataHost = "10.0.2.2", DataPort = "52953", DataPathPrefix = "/registrar")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "http", DataHost = "192.168.0.102", DataPort = "52953", DataPathPrefix = "/iniciar-sesion")]
    //[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "http", DataHost = "192.168.0.102", DataPort = "52953", DataPathPrefix = "/registrar")]
    public class DeepLinkActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var uri = Intent?.Data;

            // Reenvía el intent a MainActivity, reutilizando la existente si la hay.
            var toMain = new Intent(this, typeof(MainActivity));
            toMain.SetAction(Intent.ActionView);
            toMain.AddCategory(Intent.CategoryDefault);
            if (uri != null)
                toMain.SetData(uri);

            // Garantiza que, si MainActivity ya está en su task, reciba OnNewIntent()
            toMain.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

            StartActivity(toMain);
            Finish();
        }
    }
}
