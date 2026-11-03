using System.Text;
using System.Text.Json;
using Gladi_Bersih.Common;
using Gladi_Bersih.Configuration;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Middleware;
using Gladi_Bersih.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

var alatImageSection = builder.Configuration.GetSection(AlatImageSettings.SectionName);
var alatImageSettings = alatImageSection.Get<AlatImageSettings>()
    ?? throw new InvalidOperationException("Konfigurasi gambar alat tidak ditemukan.");

if (alatImageSettings.MaxSizeBytes <= 0)
{
    throw new InvalidOperationException("Batas ukuran gambar alat harus lebih dari 0 byte.");
}

const long multipartOverheadAllowance = 1024 * 1024;
var multipartRequestLimit = checked(alatImageSettings.MaxSizeBytes + multipartOverheadAllowance);

builder.Services.Configure<AlatImageSettings>(alatImageSection);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = multipartRequestLimit;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = multipartRequestLimit;
});

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Konfigurasi JWT tidak ditemukan.");

if (Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
{
    throw new InvalidOperationException("JWT secret minimal 32 byte.");
}

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = string.Join("; ", context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "Data yang dikirim tidak valid."
                : error.ErrorMessage));

        return new BadRequestObjectResult(ApiResponseFactory.Error(message));
    };
});

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
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(
                    ApiResponseFactory.Error("Autentikasi diperlukan."),
                    context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(
                    ApiResponseFactory.Error("Anda tidak memiliki akses ke fitur ini."),
                    context.HttpContext.RequestAborted);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API Peminjaman Alat",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Masukkan token JWT."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBootstrapService, BootstrapService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IKategoriService, KategoriService>();
builder.Services.AddScoped<IAlatService, AlatService>();
builder.Services.AddScoped<IUnitAlatService, UnitAlatService>();
builder.Services.AddScoped<IPeminjamanService, PeminjamanService>();
builder.Services.AddScoped<IPengembalianService, PengembalianService>();
builder.Services.AddScoped<ILogAktivitasService, LogAktivitasService>();
builder.Services.AddScoped<ILaporanService, LaporanService>();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapPost("/api/bootstrap/admin", async (
        [FromBody] BootstrapAdminRequest request,
        IBootstrapService service,
        CancellationToken cancellationToken) =>
    {
        var result = await service.CreateFirstAdminAsync(request, cancellationToken);
        return Results.Json(
            ApiResponseFactory.Success("Admin pertama berhasil dibuat.", result),
            statusCode: StatusCodes.Status201Created);
    })
    .AllowAnonymous()
    .WithTags("Bootstrap")
    .WithName("CreateFirstAdmin");
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
