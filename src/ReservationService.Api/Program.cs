using ReservationService.Domain;
using ReservationService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required");

var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-dev-key-change-me-in-prod-32chars!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "hotelier-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "hotelier";
var rabbitHost = builder.Configuration["Rabbit:Host"] ?? "rabbitmq";
var rabbitUser = builder.Configuration["Rabbit:Username"] ?? "guest";
var rabbitPass = builder.Configuration["Rabbit:Password"] ?? "guest";

builder.Services.AddDbContext<ReservationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumers(typeof(ReservationServiceInfrastructure).Assembly);

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHttpClient<IAccommodationServiceClient, AccommodationServiceClient>(client =>
{
    var baseUrl = builder.Configuration["Services:Accommodation"] ?? "http://accommodation-service:8080";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHttpClient<IAvailabilityServiceClient, AvailabilityServiceClient>(client =>
{
    var baseUrl = builder.Configuration["Services:Availability"] ?? "http://availability-service:8080";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("System.Net.Http")
        .AddMeter("System.Net.NameResolution")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("MassTransit"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReservationDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.MapGet("/health", () => "OK");
app.MapGet("/test", () => new { message = "Reservation service running" });

await app.RunAsync();
