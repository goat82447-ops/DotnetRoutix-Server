using DotnetRoutix.Server.Application.Validators;
using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Application.Services;
using DotnetRoutix.Server.Infrastructure.Configuration;
using DotnetRoutix.Server.Infrastructure.Repositories;
using DotnetRoutix.Server.Infrastructure.Seeding;
using DotnetRoutix.Server.Infrastructure.Services;
using FluentValidation;
using FluentValidation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoDbOptions>(builder.Configuration.GetSection(MongoDbOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<IAuthRepository, MongoAuthRepository>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IAuthSeeder, AuthSeeder>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

var app = builder.Build();

app.UseCors();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new
{
    service = "DotnetRoutix Auth Service",
    version = "v1",
    endpoints = "/api/auth/demo-user, /api/auth/login, /api/auth/verify-pin"
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "dotnetroutix-auth-service",
    status = "ok",
    timestamp = DateTime.UtcNow.ToString("o")
}));

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IAuthSeeder>();
    await seeder.SeedAsync();
}

app.MapControllers();

app.Run();
