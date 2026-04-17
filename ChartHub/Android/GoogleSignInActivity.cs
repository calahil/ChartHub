#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;

namespace ChartHub
{

    [Activity(
        Name = "com.calahilstudios.charthub.OAuthRedirectActivity",
        Exported = true,
        NoHistory = true,
        LaunchMode = Android.Content.PM.LaunchMode.SingleTop)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = OAuthRedirectActivity.RedirectScheme,
        DataPathPrefix = "/oauth2redirect")]
    internal sealed class OAuthRedirectActivity : Activity
    {
        // Must match the reverse-DNS form of the Android OAuth client ID: com.googleusercontent.apps.<client_id_prefix>
        // This value is a compile-time constant (required for Android IntentFilter attributes) and must be
        // updated to match the client ID configured in GoogleDrive:android_client_id / GOOGLEDRIVE_ANDROID_CLIENT_ID.
        // Do NOT commit a real client ID here. Obtain the correct value from your team's secrets store.
        internal const string RedirectScheme = "com.googleusercontent.apps.REPLACE_WITH_ANDROID_CLIENT_ID_PREFIX";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            ChartHub.Services.AndroidOAuthRedirectBridge.HandleIntent(Intent);
            Finish();
        }
    }

}

namespace ChartHub.Services
{

    internal static class AndroidOAuthRedirectBridge
    {
        private static TaskCompletionSource<string>? _tcs;

        internal static Task<string> WaitForCodeAsync(CancellationToken cancellationToken)
        {
            _tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
            return _tcs.Task;
        }

        internal static void LaunchAuthorizationUri(string authorizationUri)
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(authorizationUri));
            intent.AddFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }

        internal static void HandleIntent(Intent? intent)
        {
            Android.Net.Uri? uri = intent?.Data;
            if (uri is null)
            {
                Fail("Google OAuth redirect failed: missing response URI.");
                return;
            }

            string? error = uri.GetQueryParameter("error");
            if (!string.IsNullOrWhiteSpace(error))
            {
                string? description = uri.GetQueryParameter("error_description");
                Fail(string.IsNullOrWhiteSpace(description)
                    ? $"Google OAuth failed: {error}."
                    : $"Google OAuth failed: {error} ({description}).");
                return;
            }

            string? code = uri.GetQueryParameter("code");
            if (string.IsNullOrWhiteSpace(code))
            {
                Fail("Google OAuth failed: missing authorization code in redirect.");
                return;
            }

            Complete(code);
        }

        internal static void Complete(string authCode) => _tcs?.TrySetResult(authCode);

        internal static void Fail(string message) => _tcs?.TrySetException(new InvalidOperationException(message));
    }

}
#endif
