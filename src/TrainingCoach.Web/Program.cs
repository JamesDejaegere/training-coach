using TrainingCoach.Infrastructure;
using TrainingCoach.Infrastructure.Storage;
using TrainingCoach.Web;
using TrainingCoach.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var dataDirectory = DataDirectoryResolver.Resolve(builder.Configuration["DataDirectory"], builder.Environment.ContentRootPath);
builder.Configuration["DataDirectory"] = dataDirectory;
builder.Services.AddInfrastructure(dataDirectory, builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.MigrateDatabaseAsync();
if (app.Environment.IsDevelopment())
{
    // One-off plan import (SPEC 5.4) for development: the seed workbook in the repository root.
    var seed = Path.GetFullPath(Path.Combine(dataDirectory, "..", "seed", "Schema_Antwerp_10_Miles_2027.xlsx"));
    await app.Services.SeedPlanIfEmptyAsync(seed);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRequestLocalization(o => o.SetDefaultCulture("nl-BE").AddSupportedCultures("nl-BE").AddSupportedUICultures("nl-BE"));

app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapStravaEndpoints();
app.MapPolarEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

