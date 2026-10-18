var builder = DistributedApplication.CreateBuilder(args);

// The AppHost launch profile (Local / HomeNetwork) selects the matching launch profile of the web project.
var webProfile = builder.Configuration["WebLaunchProfile"] ?? "Local";

builder.AddProject<Projects.TrainingCoach_Web>("web", launchProfileName: webProfile);

builder.Build().Run();
