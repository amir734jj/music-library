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
using EfCoreRepository.Extensions;
using Serilog;
using StreamRipper.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, configuration) => configuration.WriteTo.Console());
var connectionString = builder.Configuration.GetConnectionString("MusicLibrary") ?? throw new InvalidOperationException("ConnectionStrings:MusicLibrary is required.");
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");

builder.Services.AddDbContext<MusicLibraryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddEfRepository<MusicLibraryDbContext>(options => options.DefaultProfiles());
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
builder.Services.AddControllers();
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
builder.Services.AddHostedService<StationProbeWorker>();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MusicLibraryDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var name in new[] { Roles.Admin, Roles.User }) if (!await roles.RoleExistsAsync(name)) await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = name });
}
app.Run();