using TVRoom.Authorization;
using TVRoom.Broadcast;
using TVRoom.Configuration;
using TVRoom.Persistence;
using TVRoom.Tuner;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Vite.AspNetCore.Extensions;
using TVRoom.Transcode;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSignalR();
builder.Services.AddRazorPages();
builder.Services.AddViteServices();

builder.Services.AddGoogleAuthenticationServices(builder.Configuration);
builder.Services.AddTVRoomAuthorizationServices();
builder.Services.AddTVRoomHlsServices(builder.Configuration);
builder.Services.AddTVRoomBroadcastServices();
builder.Services.AddTunerServices(builder.Configuration);
builder.Services.AddConfigurationServices(builder.Configuration);
builder.Services.AddDbContext<TVRoomContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TVRoomContext")));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
});

builder.Services.AddTransient(p => p.GetRequiredService<ILoggerFactory>().CreateLogger("EndpointLogger"));

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", builder =>
        builder
            .AllowAnyHeader()
            .AllowAnyOrigin()
            .AllowAnyMethod()));

var app = builder.Build();

// Create and migrate database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TVRoomContext>();
    await context.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days.
    app.UseHsts();
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseViteDevelopmentServer(useMiddleware: true);
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapHub<ControlPanelHub>("/controlPanelHub");
app.MapBroadcastApiEndpoints();
app.MapTranscodeApiEndpoints();
app.MapBroadcastLogEndpoints();
app.MapTunerApiEndpoints();
app.MapConfigurationApiEndpoints();
app.MapUserApiEndpoints();

app.Run();
