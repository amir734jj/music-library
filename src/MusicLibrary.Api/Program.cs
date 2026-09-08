using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MusicLibrary.Api.Data;
using MusicLibrary.Api.Infrastructure;
using MusicLibrary.Api.Migrations;
using MusicLibrary.Api.Services;
using MusicLibrary.Api.Workers;
using MusicLibrary.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using EfCoreRepository.Extensions;
using FluentMigrator.Runner;
using Serilog;
using StreamRipper.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "music-library-api")
    .WriteTo.Console());
var databaseUrl = builder.Configuration["DATABASE_URL"] ?? throw new InvalidOperationException("DATABASE_URL is required.");
var connectionString = DatabaseUrlConverter.ToConnectionString(databaseUrl);
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is required.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required.");

builder.Services.AddDbContext<MusicLibraryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddFluentMigratorCore().ConfigureRunner(runner => runner
    .AddPostgres()
    .WithGlobalConnectionString(connectionString)
    .ScanIn(typeof(InitialDatabase).Assembly).For.Migrations());
builder.Services.AddEfRepository<MusicLibraryDbContext>(options => options.DefaultProfiles());
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<MusicLibraryDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer, ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var authLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MusicLibrary.Api.Authentication");
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = userId is null ? null : await userManager.FindByIdAsync(userId);
            if (user is null || !user.IsActive)
            {
                authLogger.LogWarning(
                    "Rejected JWT for {Path}: subject {UserId} was missing or inactive.",
                    context.HttpContext.Request.Path,
                    userId ?? "missing");
                context.Fail("The account is disabled or no longer exists.");
                return;
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            var tokenRoles = context.Principal!.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
            if (!tokenRoles.SetEquals(currentRoles))
            {
                authLogger.LogWarning("Rejected JWT for {Path}: roles changed for user {UserId}.", context.HttpContext.Request.Path, user.Id);
                context.Fail("The account roles have changed. Sign in again.");
            }
        },
        OnAuthenticationFailed = context =>
        {
            var authLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("MusicLibrary.Api.Authentication");
            authLogger.LogWarning(context.Exception, "JWT authentication failed for {Path}.", context.HttpContext.Request.Path);
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
    options.SerializerSettings.Converters.Add(new StringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// StationDirectoryImportService is registered via AddHttpClient below, so exclude it from scanning.
builder.Services.Scan(scan => scan
    .FromAssemblyOf<IGlobalConfigService>()
    .AddClasses(classes => classes.InNamespaces("MusicLibrary.Api.Services").Where(type => type != typeof(StationDirectoryImportService)))
    .AsMatchingInterface()
    .WithScopedLifetime());
builder.Services.AddStreamRipper();
builder.Services.AddHttpClient<IStationDirectoryImportService, StationDirectoryImportService>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient("LiveStreamProxy", client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<StationProbeStatusStore>();
builder.Services.AddSingleton<TrackCaptureQueue>();
builder.Services.AddSingleton<TrackCacheStorage>();
builder.Services.AddSingleton<LiveStreamTicketStore>();
builder.Services.AddHostedService<StationProbeWorker>();
builder.Services.AddHostedService<EncryptedTrackCacheWorker>();

var app = builder.Build();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("User", httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity.Name ?? "authenticated"
            : "anonymous");
    };
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
var browserStaticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var extension = Path.GetExtension(context.File.Name);
        if (!extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".js", StringComparison.OrdinalIgnoreCase)) return;

        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(browserStaticFileOptions);

app.MapFallback("api/{**rest}", async context =>
{
    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
    await context.Response.WriteAsync($"Failed to find the endpoint for {context.Request.Method}:{context.Request.GetDisplayUrl()}");
});

if (app.Environment.IsDevelopment())
{
    app.MapFallback(() => Results.Text("Music Library API server is running."));
}
else
{
    app.MapFallbackToFile("index.html", browserStaticFileOptions);
}

using (var scope = app.Services.CreateScope())
{
    app.Logger.LogInformation("Applying Music Library PostgreSQL migrations and roles.");
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().EnsureTrendingCacheEncryptionKeyAsync(default);
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var name in new[] { Roles.Admin, Roles.User }) if (!await roles.RoleExistsAsync(name)) await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = name });
    app.Logger.LogInformation("Music Library database and roles are ready.");
}
app.Logger.LogInformation("Starting Music Library API in {Environment}.", app.Environment.EnvironmentName);
await app.RunAsync();