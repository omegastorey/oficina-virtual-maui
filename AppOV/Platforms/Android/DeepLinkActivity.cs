using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System;

namespace AppOV
{
    // Activity puente Edelap
    [Activity(
        Name = "com.storey.AppOV.Edelap.DeepLinkActivity",
        Theme = "@style/Maui.SplashTheme",
        LaunchMode = LaunchMode.SingleTop,
        NoHistory = true,
        Exported = true)]
    public class DeepLinkActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var uri = Intent?.Data;

            // Método virtual para decidir a qué MainActivity ir
            var toMain = new Intent(this, GetMainActivityType());
            toMain.SetAction(Intent.ActionView);
            toMain.AddCategory(Intent.CategoryDefault);
            if (uri != null)
                toMain.SetData(uri);
            // Garantiza que, si la MainActivity ya está en su task, reciba OnNewIntent()
            toMain.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            StartActivity(toMain);
            Finish();
        }
        // Por defecto, apuntamos a la MainActivity
        protected virtual Type GetMainActivityType() => typeof(MainActivity);
    }
    [Activity(
        Name = "com.storey.AppOV.Cashpower.DeepLinkActivity", 
        Theme = "@style/Maui.SplashTheme",
        LaunchMode = LaunchMode.SingleTop,
        NoHistory = true,
        Exported = true)]
    public class CashpowerDeepLinkActivity : DeepLinkActivity
    {
        protected override Type GetMainActivityType() => typeof(CashpowerMainActivity);
    }
}
