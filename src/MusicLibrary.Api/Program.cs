using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using MusicLibrary.Api.Workers;
using MusicLibrary.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, configuration) => configuration.WriteTo.Console());
var connectionString = builder.Configuration.GetConnectionString("MusicLibrary") ?? throw new InvalidOperationException("ConnectionStrings:MusicLibrary is required.");
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");

builder.Services.AddDbContext<MusicLibraryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<MusicLibraryDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
    ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IGlobalConfigService, GlobalConfigService>();
builder.Services.AddScoped<IStreamMetadataProbe, StreamMetadataProbe>();
builder.Services.AddHttpClient<IStationDirectoryImportService, StationDirectoryImportService>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHostedService<StationProbeWorker>();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/auth/register", async (RegisterRequest request, UserManager<ApplicationUser> users) =>
{
    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName };
    var result = await users.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(error => error.Code).ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
    await users.AddToRoleAsync(user, Roles.User);
    return Results.Created($"/api/admin/users/{user.Id}", new UserSummary(user.Id, user.Email!, user.DisplayName, [Roles.User], user.IsActive));
});
app.MapPost("/api/auth/login", async (LoginRequest request, UserManager<ApplicationUser> users) =>
{
    var user = await users.FindByEmailAsync(request.Email);
    if (user is null || !user.IsActive || !await users.CheckPasswordAsync(user, request.Password)) return Results.Unauthorized();
    user.LastLoginAt = DateTimeOffset.UtcNow;
    await users.UpdateAsync(user);
    var roles = await users.GetRolesAsync(user);
    var expiresAt = DateTimeOffset.UtcNow.AddHours(12);
    var token = new JwtSecurityToken(builder.Configuration["Jwt:Issuer"], builder.Configuration["Jwt:Audience"],
        [new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(ClaimTypes.Name, user.Email!), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
        expires: expiresAt, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
    return Results.Ok(new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, new UserSummary(user.Id, user.Email!, user.DisplayName, roles, user.IsActive)));
});

var userApi = app.MapGroup("/api").RequireAuthorization();
userApi.MapGet("/now-playing", async (MusicLibraryDbContext db, CancellationToken ct) => await db.PlayObservations.AsNoTracking().OrderByDescending(play => play.ObservedAt).Take(100)
    .Select(play => new NowPlayingSummary(play.StationId, play.Station.Name, play.Artist, play.Title, play.RawMetadata, play.ObservedAt, play.Confidence)).ToListAsync(ct));
userApi.MapGet("/subscriptions", async (ClaimsPrincipal principal, MusicLibraryDbContext db, CancellationToken ct) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    return await db.ArtistSubscriptions.Where(subscription => subscription.UserId == userId).OrderBy(subscription => subscription.ArtistName)
        .Select(subscription => new ArtistSubscriptionSummary(subscription.Id, subscription.ArtistName, subscription.CreatedAt, subscription.CaptureEnabled)).ToListAsync(ct);
});
userApi.MapPost("/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal principal, MusicLibraryDbContext db, CancellationToken ct) =>
{
    var artist = request.ArtistName.Trim();
    if (artist.Length is < 2 or > 200) return Results.ValidationProblem(new Dictionary<string, string[]> { ["artistName"] = ["Artist name must be between 2 and 200 characters."] });
    var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var normalizedArtist = artist.ToUpperInvariant();
    if (await db.ArtistSubscriptions.AnyAsync(subscription => subscription.UserId == userId && subscription.NormalizedArtistName == normalizedArtist, ct)) return Results.Conflict();
    var subscription = new ArtistSubscription { Id = Guid.NewGuid(), UserId = userId, ArtistName = artist, NormalizedArtistName = normalizedArtist, CaptureEnabled = request.CaptureEnabled, CreatedAt = DateTimeOffset.UtcNow };
    db.ArtistSubscriptions.Add(subscription);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/subscriptions/{subscription.Id}", new ArtistSubscriptionSummary(subscription.Id, subscription.ArtistName, subscription.CreatedAt, subscription.CaptureEnabled));
});
userApi.MapGet("/alerts", async (ClaimsPrincipal principal, MusicLibraryDbContext db, CancellationToken ct) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    return await db.UserAlerts.Where(alert => alert.UserId == userId).OrderByDescending(alert => alert.CreatedAt).Take(100)
        .Select(alert => new UserAlertSummary(alert.Id, alert.ArtistSubscription.ArtistName, alert.PlayObservation.Station.Name, alert.PlayObservation.Title, alert.PlayObservation.ObservedAt)).ToListAsync(ct);
});

var adminApi = app.MapGroup("/api/admin").RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
adminApi.MapGet("/users", async (UserManager<ApplicationUser> users, CancellationToken ct) =>
{
    var accounts = await users.Users.AsNoTracking().OrderBy(user => user.Email).ToListAsync(ct);
    var summaries = await Task.WhenAll(accounts.Select(async user => new UserSummary(user.Id, user.Email!, user.DisplayName, await users.GetRolesAsync(user), user.IsActive)));
    return Results.Ok(summaries);
});
adminApi.MapGet("/config", (IGlobalConfigService config, CancellationToken ct) => config.GetAsync(ct));
adminApi.MapPut("/config", async (UpdateGlobalConfigRequest request, ClaimsPrincipal principal, IGlobalConfigService config, CancellationToken ct) =>
{
    await config.SaveAsync(request.Values, Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!), ct);
    return Results.NoContent();
});
adminApi.MapPost("/stations/import", async (IStationDirectoryImportService importService, CancellationToken ct) => Results.Ok(await importService.ImportAsync(ct)));
adminApi.MapGet("/stations", async (MusicLibraryDbContext db, CancellationToken ct) => await db.Stations.AsNoTracking().OrderBy(station => station.Name)
    .Select(station => new StationSummary(station.Id, station.Name, station.Genre, station.StreamUrl, station.IsProbeEnabled, station.LastProbedAt)).ToListAsync(ct));
adminApi.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest request, UserManager<ApplicationUser> users) =>
{
    var user = await users.FindByIdAsync(id.ToString());
    if (user is null) return Results.NotFound();
    user.DisplayName = request.DisplayName?.Trim();
    user.IsActive = request.IsActive;
    var update = await users.UpdateAsync(user);
    if (!update.Succeeded) return Results.ValidationProblem(update.Errors.GroupBy(error => error.Code).ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
    if (!string.IsNullOrWhiteSpace(request.Role) && request.Role is not Roles.Admin and not Roles.User) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Role must be Admin or User."] });
    if (!string.IsNullOrWhiteSpace(request.Role))
    {
        var existingRoles = await users.GetRolesAsync(user);
        await users.RemoveFromRolesAsync(user, existingRoles);
        await users.AddToRoleAsync(user, request.Role);
    }
    return Results.NoContent();
});

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MusicLibraryDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var name in new[] { Roles.Admin, Roles.User }) if (!await roles.RoleExistsAsync(name)) await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = name });
    var bootstrapEmail = builder.Configuration["BootstrapAdmin:Email"];
    var bootstrapPassword = builder.Configuration["BootstrapAdmin:Password"];
    if (!string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPassword))
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = await users.FindByEmailAsync(bootstrapEmail);
        if (administrator is null)
        {
            administrator = new ApplicationUser { Id = Guid.NewGuid(), UserName = bootstrapEmail, Email = bootstrapEmail, DisplayName = "Administrator" };
            var result = await users.CreateAsync(administrator, bootstrapPassword);
            if (!result.Succeeded) throw new InvalidOperationException($"Unable to create bootstrap administrator: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
        if (!await users.IsInRoleAsync(administrator, Roles.Admin)) await users.AddToRoleAsync(administrator, Roles.Admin);
    }
}
app.Run();