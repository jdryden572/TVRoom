using LivingRoom.Authorization;
using LivingRoom.Broadcast;
using LivingRoom.Tuner;
using Microsoft.AspNetCore.HttpOverrides;
using Vite.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSignalR();
builder.Services.AddRazorPages();
builder.Services.AddViteServices();

builder.Services.AddGoogleAuthenticationServices(builder.Configuration);
builder.Services.AddLivingRoomAuthorizationServices();
builder.Services.AddLivingRoomBroadcastServices(builder.Configuration);
builder.Services.AddTunerServices(builder.Configuration);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
});

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", builder =>
        builder
            .AllowAnyHeader()
            .AllowAnyOrigin()
            .AllowAnyMethod()));

var app = builder.Build();

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
app.MapBroadcastApiEndpoints(builder.Configuration);
app.MapTunerApiEndpoints();

app.Run();
