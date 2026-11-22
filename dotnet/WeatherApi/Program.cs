using System.Text.Json;
using GraphQL;
using GraphQL.SystemTextJson;
using WeatherApi;

var builder = WebApplication.CreateBuilder(args);
var source = (Environment.GetEnvironmentVariable("WEATHER_SOURCE") ?? "live").ToLowerInvariant();
if (source is not ("live" or "seed")) throw new InvalidOperationException("WEATHER_SOURCE must be \"live\" or \"seed\"");
var port = Environment.GetEnvironmentVariable("PORT") ?? "4000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton(new SeedWeatherProvider(Path.Combine(AppContext.BaseDirectory, "data", "seed.json")));
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
builder.Services.AddSingleton<IWeatherProvider>(services => source == "seed"
    ? services.GetRequiredService<SeedWeatherProvider>()
    : new LiveWeatherProvider(services.GetRequiredService<HttpClient>(), services.GetRequiredService<SeedWeatherProvider>()));
builder.Services.AddSingleton<WeatherQuery>();
builder.Services.AddSingleton<WeatherSchema>();
builder.Services.AddTransient<TravelWeatherType>();
builder.Services.AddTransient<LocationType>();
builder.Services.AddTransient<CurrentWeatherType>();
builder.Services.AddTransient<ForecastDayType>();
builder.Services.AddTransient<AirQualityType>();
builder.Services.AddTransient<WeatherAlertType>();
builder.Services.AddSingleton<IDocumentExecuter, DocumentExecuter>();
builder.Services.AddSingleton<GraphQLSerializer>();

var app = builder.Build();
app.MapGet("/", () => Results.Content(Docs.Html, "text/html"));
app.MapGet("/docs", () => Results.Content(Docs.Html, "text/html"));
app.MapGet("/openapi.json", () => Results.Content(Docs.OpenApi, "application/json"));
app.MapMethods("/graphql", ["GET", "PUT", "PATCH", "DELETE", "OPTIONS"], () =>
    Results.Json(new { error = "Use POST for GraphQL requests" }, statusCode: 405));
app.MapPost("/graphql", async (HttpContext http, WeatherSchema schema, IDocumentExecuter executer, GraphQLSerializer serializer) =>
{
    const int limit = 64 * 1024;
    if (http.Request.ContentLength > limit)
        return Results.Json(new { error = "Request body too large" }, statusCode: 413);
    using var body = new MemoryStream();
    var buffer = new byte[8192];
    int read;
    while ((read = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
    {
        if (body.Length + read > limit)
            return Results.Json(new { error = "Request body too large" }, statusCode: 413);
        body.Write(buffer, 0, read);
    }
    JsonDocument document;
    try { document = JsonDocument.Parse(body.ToArray()); }
    catch (JsonException) { return Results.Json(new { error = "Invalid JSON" }, statusCode: 400); }
    using (document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("query", out var query) ||
            query.ValueKind != JsonValueKind.String ||
            (root.TryGetProperty("variables", out var variables) && variables.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) ||
            (root.TryGetProperty("operationName", out var operationName) && operationName.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)))
            return Results.Json(new { error = "Invalid GraphQL request" }, statusCode: 400);
        var input = root.TryGetProperty("variables", out variables) && variables.ValueKind == JsonValueKind.Object
            ? serializer.Deserialize<Inputs>(variables.GetRawText()) : null;
        var result = await executer.ExecuteAsync(options =>
        {
            options.Schema = schema;
            options.Query = query.GetString();
            options.Variables = input;
            options.OperationName = root.TryGetProperty("operationName", out operationName) && operationName.ValueKind == JsonValueKind.String ? operationName.GetString() : null;
            options.CancellationToken = http.RequestAborted;
        });
        return Results.Text(serializer.Serialize(result), "application/json", statusCode: 200);
    }
});
app.Run();

public partial class Program;
