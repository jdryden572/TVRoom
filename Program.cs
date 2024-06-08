using LivingRoom.Authorization;
using LivingRoom.Broadcast;
using LivingRoom.Tuner;
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days.
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseViteDevelopmentServer(useMiddleware: true);
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapHub<ControlPanelHub>("/controlPanelHub");
app.MapBroadcastApiEndpoints(builder.Configuration);
app.MapTunerApiEndpoints();

app.Run();
