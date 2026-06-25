using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace PersonalAssistant.Features.Widgets;

public partial class WeatherWidget : UserControl
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private DateTime _lastFetch = DateTime.MinValue;
    private string _cachedInfo = "--";
    private string _cachedLocation = "";

    public WeatherWidget()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshWeatherAsync();
    }

    public async Task RefreshWeatherAsync()
    {
        if ((DateTime.Now - _lastFetch).TotalMinutes < 30 && _cachedInfo != "--")
        {
            WeatherInfo.Text = _cachedInfo;
            WeatherLocation.Text = _cachedLocation;
            return;
        }

        try
        {
            var response = await _http.GetStringAsync("https://wttr.in/?format=%t+%C");
            var parts = response.Trim().Split(' ', 2);
            if (parts.Length >= 2)
            {
                _cachedInfo = parts[0];
                _cachedLocation = parts[1];
                _lastFetch = DateTime.Now;
            }
            else
            {
                _cachedInfo = response.Trim();
                _cachedLocation = "";
                _lastFetch = DateTime.Now;
            }
        }
        catch
        {
            _cachedInfo = "--";
            _cachedLocation = "无法获取天气";
        }

        Dispatcher.Invoke(() =>
        {
            WeatherInfo.Text = _cachedInfo;
            WeatherLocation.Text = _cachedLocation;
        });
    }
}
