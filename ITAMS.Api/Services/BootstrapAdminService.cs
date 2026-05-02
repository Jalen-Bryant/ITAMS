using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class BootstrapAdminService
{
    private readonly BootstrapAdminSettings _settings;
    private readonly IPasswordHasher<UserDocument> _passwordHasher;
    private readonly UsersService _usersService;
    private readonly IMongoCollection<UserDocument> _usersCollection;
    private readonly ILogger<BootstrapAdminService> _logger;

    public BootstrapAdminService(
        IMongoClient mongoClient,
        IOptions<MongoDbSettings> mongoDbSettings,
        IOptions<BootstrapAdminSettings> settings,
        IPasswordHasher<UserDocument> passwordHasher,
        UsersService usersService,
        ILogger<BootstrapAdminService> logger)
    {
        _settings = settings.Value;
        _passwordHasher = passwordHasher;
        _usersService = usersService;
        _logger = logger;

        var database = mongoClient.GetDatabase(mongoDbSettings.Value.DatabaseName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.Value.UsersCollectionName);
    }

    public async Task EnsureBootstrapAdminAsync(CancellationToken cancellationToken = default)
    {
        var totalUsers = await _usersService.CountAsync(cancellationToken);
        var loginCapableUsers = await _usersService.CountUsersWithPasswordHashAsync(cancellationToken);

        // Startup only seeds credentials when nobody can log in yet; otherwise bootstrap settings stay dormant.
        EnsureBootstrapCanRun(loginCapableUsers, _settings);

        if (loginCapableUsers > 0)
        {
            return;
        }

        var normalizedUsername = UsersService.NormalizeValue(_settings.Username);
        var normalizedEmail = UsersService.NormalizeValue(_settings.Email);
        var existingUser = await _usersService.GetByIdentifierAsync(_settings.Username, cancellationToken) ??
                           await _usersService.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (existingUser is not null)
        {
            if (!string.IsNullOrWhiteSpace(existingUser.PasswordHash))
            {
                return;
            }

            var now = DateTime.UtcNow;
            var provisionedUser = new UserDocument
            {
                Id = existingUser.Id,
                Username = _settings.Username.Trim(),
                DisplayName = _settings.DisplayName.Trim(),
                Email = _settings.Email.Trim(),
                NormalizedUsername = normalizedUsername,
                NormalizedEmail = normalizedEmail,
                PasswordHash = _passwordHasher.HashPassword(existingUser, _settings.Password),
                PasswordChangedAt = now,
                Role = "Admin",
                Department = _settings.Department.Trim(),
                IsActive = true,
                CreatedAt = existingUser.CreatedAt,
                UpdatedAt = now
            };

            await _usersCollection.ReplaceOneAsync(
                user => user.Id == existingUser.Id,
                provisionedUser,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Provisioned bootstrap admin credentials for existing user '{Username}'.", provisionedUser.Username);
            return;
        }

        var bootstrapUser = new UserDocument
        {
            Id = ObjectId.GenerateNewId(),
            Username = _settings.Username.Trim(),
            DisplayName = _settings.DisplayName.Trim(),
            Email = _settings.Email.Trim(),
            NormalizedUsername = normalizedUsername,
            NormalizedEmail = normalizedEmail,
            PasswordHash = null,
            PasswordChangedAt = DateTime.UtcNow,
            Role = "Admin",
            Department = _settings.Department.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var passwordHash = _passwordHasher.HashPassword(bootstrapUser, _settings.Password);
        bootstrapUser = new UserDocument
        {
            Id = bootstrapUser.Id,
            Username = bootstrapUser.Username,
            DisplayName = bootstrapUser.DisplayName,
            Email = bootstrapUser.Email,
            NormalizedUsername = bootstrapUser.NormalizedUsername,
            NormalizedEmail = bootstrapUser.NormalizedEmail,
            PasswordHash = passwordHash,
            PasswordChangedAt = bootstrapUser.PasswordChangedAt,
            Role = bootstrapUser.Role,
            Department = bootstrapUser.Department,
            IsActive = bootstrapUser.IsActive,
            CreatedAt = bootstrapUser.CreatedAt,
            UpdatedAt = bootstrapUser.UpdatedAt
        };

        await _usersCollection.InsertOneAsync(bootstrapUser, cancellationToken: cancellationToken);

        _logger.LogInformation(
            totalUsers == 0
                ? "Seeded bootstrap admin user '{Username}' into an empty users collection."
                : "Seeded bootstrap admin user '{Username}' because no login-capable users existed.",
            bootstrapUser.Username);
    }

    internal static void EnsureBootstrapCanRun(long loginCapableUsers, BootstrapAdminSettings settings)
    {
        if (loginCapableUsers > 0)
        {
            return;
        }

        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "No login-capable users exist. Configure BootstrapAdmin settings with a username, display name, email, department, and password before starting the API.");
        }
    }
}
