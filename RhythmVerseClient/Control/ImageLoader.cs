using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace RhythmVerseClient.Control
{
    public class BindableImage : Image
    {

        public static readonly BindableProperty ImageUrlProperty =
            BindableProperty.Create(
                nameof(ImageUrl),
                typeof(string),
                typeof(BindableImage),
                default(string),
                propertyChanged: OnImageUrlChanged);

        public string ImageUrl
        {
            get => (string)GetValue(ImageUrlProperty);
            set => SetValue(ImageUrlProperty, value);
        }

        private static async void OnImageUrlChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (BindableImage)bindable;
            string newUrl = (string)newValue;

            if (await IsImageUrlValidAsync(newUrl))
            {
                control.Source = ImageSource.FromUri(new Uri(newUrl));
            }
            else
            {
                control.Source = "noalbumart.png"; // Path to your placeholder image
            }
        }

        private static async Task<bool> IsImageUrlValidAsync(string url)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(url);
                    return response.IsSuccessStatusCode && response.Content.Headers.ContentType.MediaType.StartsWith("image/");
                }
            }
            catch
            {
                return false;
            }
        }
    }

}
