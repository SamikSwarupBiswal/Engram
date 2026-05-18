using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Google;

/// <summary>
/// Fetches Google Calendar METADATA only — event title, time, attendees.
/// Uses calendar.readonly scope.
/// </summary>
public class CalendarMetadataProvider
{
    private readonly GoogleOAuthManager _oauth;
    private readonly HttpClient _http;
    private readonly ILogger<CalendarMetadataProvider>? _logger;

    public CalendarMetadataProvider(GoogleOAuthManager oauth, HttpClient http, ILogger<CalendarMetadataProvider>? logger = null)
    {
        _oauth = oauth;
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetch upcoming calendar events (next N days).
    /// Returns title, time, attendees — no event body/description.
    /// </summary>
    public async Task<List<CalendarEventMetadata>> GetUpcomingEventsAsync(int days = 7, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        var token = await _oauth.GetAccessTokenAsync(cancellationToken);
        if (token == null) return new List<CalendarEventMetadata>();

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var timeMin = DateTimeOffset.UtcNow.ToString("o");
            var timeMax = DateTimeOffset.UtcNow.AddDays(days).ToString("o");
            var url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events?timeMin={timeMin}&timeMax={timeMax}&maxResults={maxResults}&singleEvents=true&orderBy=startTime";

            var response = await _http.GetFromJsonAsync<CalendarEventList>(url, cancellationToken);
            if (response?.Items == null) return new List<CalendarEventMetadata>();

            var events = response.Items.Select(e => new CalendarEventMetadata
            {
                EventId = e.Id ?? "",
                Title = e.Summary ?? "(untitled)",
                StartTime = e.Start?.DateTime ?? e.Start?.Date ?? "",
                EndTime = e.End?.DateTime ?? e.End?.Date ?? "",
                Attendees = e.Attendees?.Select(a => a.Email ?? "").Where(e => e.Length > 0).ToList() ?? new List<string>(),
                Location = e.Location ?? "",
                Status = e.Status ?? "confirmed",
                IsAllDay = e.Start?.Date != null && e.Start?.DateTime == null
            }).ToList();

            _logger?.LogInformation("Fetched {Count} calendar events", events.Count);
            return events;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fetch calendar metadata");
            return new List<CalendarEventMetadata>();
        }
    }
}

public class CalendarEventMetadata
{
    public string EventId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string StartTime { get; init; } = string.Empty;
    public string EndTime { get; init; } = string.Empty;
    public List<string> Attendees { get; init; } = new();
    public string Location { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsAllDay { get; init; }
}

// Calendar API response models
public class CalendarEventList
{
    public List<CalendarEvent>? Items { get; set; }
}

public class CalendarEvent
{
    public string? Id { get; set; }
    public string? Summary { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public CalendarDateTime? Start { get; set; }
    public CalendarDateTime? End { get; set; }
    public List<CalendarAttendee>? Attendees { get; set; }
}

public class CalendarDateTime
{
    public string? DateTime { get; set; }
    public string? Date { get; set; }
}

public class CalendarAttendee
{
    public string? Email { get; set; }
}
