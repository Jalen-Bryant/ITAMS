using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class AuthService
{
    private const string InvalidCredentialsMessage = "The supplied credentials are invalid.";
    private const string InvalidRefreshTokenMessage = "The supplied refresh token is invalid.";

    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<AuditLogDocument> _auditLogsCollection;
    private readonly IMongoCollection<UserSessionDocument> _sessionsCollection;
    private readonly IMongoCollection<UserDocument> _usersCollection;
    private readonly IPasswordHasher<UserDocument> _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly UsersService _usersService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IMongoClient mongoClient,
        IOptions<MongoDbSettings> settings,
        IPasswordHasher<UserDocument> passwordHasher,
        TokenService tokenService,
        UsersService usersService,
        ILogger<AuthService> logger)
    {
        _mongoClient = mongoClient;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _usersService = usersService;
        _logger = logger;

        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _auditLogsCollection = database.GetCollection<AuditLogDocument>(mongoDbSettings.AuditLogsCollectionName);
        _sessionsCollection = database.GetCollection<UserSessionDocument>(mongoDbSettings.UserSessionsCollectionName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.UsersCollectionName);
    }

    public async Task<AuthResult> LoginAsync(
        string identifier,
        string password,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var user = await _usersService.GetByIdentifierAsync(identifier, cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for identifier '{Identifier}'.", identifier);
            return new AuthResult { Error = InvalidCredentialsMessage };
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Failed login attempt for identifier '{Identifier}'.", identifier);
            return new AuthResult { Error = InvalidCredentialsMessage };
        }

        var now = DateTime.UtcNow;
        var sessionId = ObjectId.GenerateNewId();
        var (refreshToken, refreshTokenHash, refreshTokenExpiresAt) = _tokenService.CreateRefreshToken();
        var (accessToken, accessTokenExpiresAt) = _tokenService.CreateAccessToken(user, sessionId);

        var sessionDocument = new UserSessionDocument
        {
            Id = sessionId,
            UserId = user.Id,
            RefreshTokenHash = refreshTokenHash,
            CreatedAt = now,
            ExpiresAt = refreshTokenExpiresAt,
            RevokedAt = null,
            CreatedByIp = ip,
            UserAgent = userAgent
        };

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        // Session creation, optional password rehashing, and the LOGIN audit record succeed or fail together.
        await _sessionsCollection.InsertOneAsync(session, sessionDocument, cancellationToken: cancellationToken);

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var newPasswordHash = _passwordHasher.HashPassword(user, password);
            var updatedUser = new UserDocument
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Email = user.Email,
                NormalizedUsername = user.NormalizedUsername,
                NormalizedEmail = user.NormalizedEmail,
                PasswordHash = newPasswordHash,
                PasswordChangedAt = user.PasswordChangedAt ?? now,
                Role = user.Role,
                Department = user.Department,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            };

            await _usersCollection.ReplaceOneAsync(
                session,
                existingUser => existingUser.Id == user.Id,
                updatedUser,
                cancellationToken: cancellationToken);

            user = updatedUser;
        }

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "LOGIN",
                user.Id,
                user.Id,
                "User",
                $"User {user.Username} logged in via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);

        return new AuthResult
        {
            Success = true,
            User = user,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = refreshTokenExpiresAt
        };
    }

    public async Task<AuthResult> RefreshAsync(
        string refreshToken,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var refreshTokenHash = TokenService.HashRefreshToken(refreshToken);
        var existingSession = await _sessionsCollection
            .Find(session => session.RefreshTokenHash == refreshTokenHash)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingSession is null || existingSession.RevokedAt is not null || existingSession.ExpiresAt <= DateTime.UtcNow)
        {
            return new AuthResult { Error = InvalidRefreshTokenMessage };
        }

        var user = await _usersService.GetByIdAsync(existingSession.UserId, cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return new AuthResult { Error = InvalidRefreshTokenMessage };
        }

        var now = DateTime.UtcNow;
        var newSessionId = ObjectId.GenerateNewId();
        var (newRefreshToken, newRefreshTokenHash, newRefreshExpiresAt) = _tokenService.CreateRefreshToken();
        var (accessToken, accessTokenExpiresAt) = _tokenService.CreateAccessToken(user, newSessionId);

        var newSession = new UserSessionDocument
        {
            Id = newSessionId,
            UserId = user.Id,
            RefreshTokenHash = newRefreshTokenHash,
            CreatedAt = now,
            ExpiresAt = newRefreshExpiresAt,
            RevokedAt = null,
            CreatedByIp = ip,
            UserAgent = userAgent
        };

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        // Refresh token rotation is one-time use: the existing session is revoked before the replacement session is created.
        var revokeUpdate = Builders<UserSessionDocument>.Update
            .Set(existing => existing.RevokedAt, now)
            .Set(existing => existing.ExpiresAt, now);
        var revokeResult = await _sessionsCollection.UpdateOneAsync(
            session,
            existing => existing.Id == existingSession.Id && existing.RevokedAt == null,
            revokeUpdate,
            cancellationToken: cancellationToken);

        if (revokeResult.MatchedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new AuthResult { Error = InvalidRefreshTokenMessage };
        }

        await _sessionsCollection.InsertOneAsync(session, newSession, cancellationToken: cancellationToken);
        await session.CommitTransactionAsync(cancellationToken);

        return new AuthResult
        {
            Success = true,
            User = user,
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = newRefreshExpiresAt
        };
    }

    public async Task LogoutAsync(
        ObjectId userId,
        ObjectId sessionId,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var existingSession = await _sessionsCollection
            .Find(session => session.Id == sessionId && session.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingSession is null)
        {
            return;
        }

        var user = await _usersService.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var now = DateTime.UtcNow;
        var revokeUpdate = Builders<UserSessionDocument>.Update
            .Set(existing => existing.RevokedAt, now)
            .Set(existing => existing.ExpiresAt, now);

        await _sessionsCollection.UpdateOneAsync(
            session,
            existing => existing.Id == sessionId && existing.UserId == userId && existing.RevokedAt == null,
            revokeUpdate,
            cancellationToken: cancellationToken);

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "LOGOUT",
                userId,
                userId,
                "User",
                $"User {user.Username} logged out via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        ObjectId userId,
        string currentPassword,
        string newPassword,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var user = await _usersCollection
            .Find(existingUser => existingUser.Id == userId)
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return new PasswordChangeResult { Error = InvalidCredentialsMessage };
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return new PasswordChangeResult { Error = InvalidCredentialsMessage };
        }

        var now = DateTime.UtcNow;
        var updatedUser = new UserDocument
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            NormalizedUsername = user.NormalizedUsername,
            NormalizedEmail = user.NormalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(user, newPassword),
            PasswordChangedAt = now,
            Role = user.Role,
            Department = user.Department,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = now
        };

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();

        var replaceResult = await _usersCollection.ReplaceOneAsync(
            session,
            existingUser => existingUser.Id == userId,
            updatedUser,
            cancellationToken: cancellationToken);
        if (replaceResult.MatchedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new PasswordChangeResult { Error = "The current user could not be updated." };
        }

        var revokeUpdate = Builders<UserSessionDocument>.Update
            .Set(existing => existing.RevokedAt, now)
            .Set(existing => existing.ExpiresAt, now);

        // A password change invalidates every outstanding session so old access and refresh tokens cannot keep working.
        await _sessionsCollection.UpdateManyAsync(
            session,
            existing => existing.UserId == userId && existing.RevokedAt == null,
            revokeUpdate,
            cancellationToken: cancellationToken);

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "UPDATE",
                userId,
                userId,
                "User",
                $"User {user.Username} changed their password via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new PasswordChangeResult { Success = true };
    }
}
