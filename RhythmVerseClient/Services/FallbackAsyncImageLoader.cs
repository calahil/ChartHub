using AsyncImageLoader;
using Avalonia.Media.Imaging;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RhythmVerseClient.Services
{
    /// <summary>
    /// Custom async image loader that provides fallback support for 404 and other HTTP errors.
    /// Wraps the default image loader to catch download failures gracefully.
    /// </summary>
    public class FallbackAsyncImageLoader : IAsyncImageLoader, IDisposable
    {
        private static readonly HttpClient ProbeClient = new();
        private readonly IAsyncImageLoader _innerLoader;
        private readonly string _avatarFallback = "avares://RhythmVerseClient/Resources/Images/blankprofile.png";
        private readonly string _albumFallback = "avares://RhythmVerseClient/Resources/Images/noalbumart.png";
        private readonly string _genericFallback = "avares://RhythmVerseClient/Resources/Images/blank.png";

        public FallbackAsyncImageLoader(IAsyncImageLoader? innerLoader = null)
        {
            // Use provided loader or default to built-in one
            _innerLoader = innerLoader ?? ImageLoader.AsyncImageLoader;
        }

        public async Task<Bitmap?> ProvideImageAsync(string url)
        {
            if (await IsMissingHttpResourceAsync(url))
            {
                return await TryLoadFallbackAsync(url);
            }

            try
            {
                // Attempt to load the primary URL
                return await _innerLoader.ProvideImageAsync(url);
            }
            catch (HttpRequestException ex) when (
                ex.Message.Contains("404") ||
                ex.Message.Contains("Not Found") ||
                ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 404 Not Found - use appropriate fallback based on URL pattern
                var fallbackUrl = DetermineFallback(url);
                if (fallbackUrl == url)
                {
                    // Fallback detection logic couldn't determine a fallback, return null
                    return null;
                }

                return await TryLoadFallbackAsync(url);
            }
            catch (HttpRequestException)
            {
                // For other HTTP errors (timeout, network, etc.), also use fallback
                var fallbackUrl = DetermineFallback(url);
                if (fallbackUrl == url)
                {
                    return null;
                }

                return await TryLoadFallbackAsync(url);
            }
            catch
            {
                // For non-HTTP errors, propagate the exception
                throw;
            }
        }

        private async Task<Bitmap?> TryLoadFallbackAsync(string originalUrl)
        {
            var fallbackUrl = DetermineFallback(originalUrl);
            if (fallbackUrl == originalUrl)
            {
                return null;
            }

            try
            {
                return await _innerLoader.ProvideImageAsync(fallbackUrl);
            }
            catch
            {
                // If fallback also fails, return null and let Avalonia handle it
                return null;
            }
        }

        private static async Task<bool> IsMissingHttpResourceAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                using var response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return true;
                }

                // Some servers do not support HEAD and return 405/501. Try lightweight GET headers then.
                if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
                    || response.StatusCode == System.Net.HttpStatusCode.NotImplemented)
                {
                    using var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var getResponse = await ProbeClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
                    return getResponse.StatusCode == System.Net.HttpStatusCode.NotFound;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string DetermineFallback(string originalUrl)
        {
            // Detect fallback type based on URL pattern
            if (originalUrl.Contains("/cp/upload/users/") || originalUrl.Contains("avatar") || originalUrl.Contains("profile"))
            {
                return _avatarFallback;
            }
            else if (originalUrl.Contains("album") || originalUrl.Contains("cover") || originalUrl.Contains("art"))
            {
                return _albumFallback;
            }
            return _genericFallback;
        }

        public void Dispose()
        {
            // Dispose the inner loader if it implements IDisposable
            if (_innerLoader is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
