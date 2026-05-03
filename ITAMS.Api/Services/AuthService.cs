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

    private readonly AuthLockoutSettings _lockoutSettings;
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<AuditLogDocument> _auditLogsCollection;
    private readonly IMongoCollection<UserSessionDocument> _sessionsCollection;
    private readonly IMongoCollection<UserDocument> _usersCollection;
    private readonly IPasswordHasher<UserDocument> _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly UsersService _usersService;
    private readonly ILogger<AuthService> _logger;
    private readonly UserDocument _passwordVerificationDummyUser;
    private readonly string _passwordVerificationDummyHash;

    public AuthService(
        IMongoClient mongoClient,
        IOptions<MongoDbSettings> settings,
        IOptions<SecuritySettings> securitySettings,
        IPasswordHasher<UserDocument> passwordHasher,
        TokenService tokenService,
        UsersService usersService,
        ILogger<AuthService> logger)
    {
        _mongoClient = mongoClient;
        _lockoutSettings = securitySettings.Value.AuthLockout;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _usersService = usersService;
        _logger = logger;

        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);
        _auditLogsCollection = database.GetCollection<AuditLogDocument>(mongoDbSettings.AuditLogsCollectionName);
        _sessionsCollection = database.GetCollection<UserSessionDocument>(mongoDbSettings.UserSessionsCollectionName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.UsersCollectionName);
        _passwordVerificationDummyUser = new UserDocument
        {
            Id = ObjectId.Empty,
            Username = "password-verification-dummy",
            DisplayName = "Password Verification Dummy",
            Email = "password-verification-dummy@example.invalid",
            NormalizedUsername = "PASSWORD-VERIFICATION-DUMMY",
            NormalizedEmail = "PASSWORD-VERIFICATION-DUMMY@EXAMPLE.INVALID",
            Role = "User",
            Department = "Security",
            IsActive = false,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch
        };
        _passwordVerificationDummyHash = _passwordHasher.HashPassword(
            _passwordVerificationDummyUser,
            Guid.NewGuid().ToString("N"));
    }

    public async Task<AuthResult> LoginAsync(
        string identifier,
        string password,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var user = await _usersService.GetByIdentifierAsync(identifier, cancellationToken);
        if (user is not null && IsLockedOut(user, now))
        {
            _logger.LogWarning("Rejected login attempt for locked account '{Identifier}'.", identifier);
            return new AuthResult { Error = InvalidCredentialsMessage };
        }

        var canLogin = user is not null &&
                       user.IsActive &&
                       !string.IsNullOrWhiteSpace(user.PasswordHash);
        var passwordUser = user ?? _passwordVerificationDummyUser;
        var verifyResult = _passwordHasher.VerifyHashedPassword(
            passwordUser,
            canLogin ? user!.PasswordHash! : _passwordVerificationDummyHash,
            password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Failed login attempt for identifier '{Identifier}'.", identifier);
            if (canLogin)
            {
                await RecordFailedLoginAsync(user!, ip, userAgent, now, cancellationToken);
            }

            return new AuthResult { Error = InvalidCredentialsMessage };
        }

        if (!canLogin)
        {
            _logger.LogWarning("Failed login attempt for identifier '{Identifier}'.", identifier);
            return new AuthResult { Error = InvalidCredentialsMessage };
        }

        var loginUser = user!;
        var sessionId = ObjectId.GenerateNewId();
        var (refreshToken, refreshTokenHash, refreshTokenExpiresAt) = _tokenService.CreateRefreshToken();
        var (accessToken, accessTokenExpiresAt) = _tokenService.CreateAccessToken(loginUser, sessionId);

        var sessionDocument = new UserSessionDocument
        {
            Id = sessionId,
            UserId = loginUser.Id,
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
            var newPasswordHash = _passwordHasher.HashPassword(loginUser, password);
            var updatedUser = new UserDocument
            {
                Id = loginUser.Id,
                Username = loginUser.Username,
                DisplayName = loginUser.DisplayName,
                Email = loginUser.Email,
                NormalizedUsername = loginUser.NormalizedUsername,
                NormalizedEmail = loginUser.NormalizedEmail,
                PasswordHash = newPasswordHash,
                PasswordChangedAt = loginUser.PasswordChangedAt ?? now,
                FailedLoginCount = 0,
                FailedLoginWindowStartedAt = null,
                LastFailedLoginAt = null,
                LockoutEndAt = null,
                Role = loginUser.Role,
                Department = loginUser.Department,
                IsActive = loginUser.IsActive,
                CreatedAt = loginUser.CreatedAt,
                UpdatedAt = loginUser.UpdatedAt
            };

            await _usersCollection.ReplaceOneAsync(
                session,
                existingUser => existingUser.Id == loginUser.Id,
                updatedUser,
                cancellationToken: cancellationToken);

            loginUser = updatedUser;
        }
        else
        {
            await ResetFailedLoginsAsync(session, loginUser.Id, now, cancellationToken);
        }

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "LOGIN",
                loginUser.Id,
                loginUser.Id,
                "User",
                $"User {loginUser.Username} logged in via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);

        return new AuthResult
        {
            Success = true,
            User = loginUser,
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
            FailedLoginCount = 0,
            FailedLoginWindowStartedAt = null,
            LastFailedLoginAt = null,
            LockoutEndAt = null,
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

    public async Task<PasswordChangeResult> ResetUserPasswordAsync(
        ObjectId actorUserId,
        ObjectId targetUserId,
        string newPassword,
        string ip,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var user = await _usersCollection
            .Find(existingUser => existingUser.Id == targetUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return new PasswordChangeResult { NotFound = true };
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
            FailedLoginCount = 0,
            FailedLoginWindowStartedAt = null,
            LastFailedLoginAt = null,
            LockoutEndAt = null,
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
            existingUser => existingUser.Id == targetUserId,
            updatedUser,
            cancellationToken: cancellationToken);
        if (replaceResult.MatchedCount == 0)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return new PasswordChangeResult { NotFound = true };
        }

        var revokeUpdate = Builders<UserSessionDocument>.Update
            .Set(existing => existing.RevokedAt, now)
            .Set(existing => existing.ExpiresAt, now);

        await _sessionsCollection.UpdateManyAsync(
            session,
            existing => existing.UserId == targetUserId && existing.RevokedAt == null,
            revokeUpdate,
            cancellationToken: cancellationToken);

        await _auditLogsCollection.InsertOneAsync(
            session,
            MutationDocumentFactory.CreateAuditLog(
                "UPDATE",
                actorUserId,
                targetUserId,
                "User",
                $"User {user.Username} password reset by an administrator via API.",
                ip,
                userAgent),
            cancellationToken: cancellationToken);

        await session.CommitTransactionAsync(cancellationToken);
        return new PasswordChangeResult { Success = true };
    }

    private static bool IsLockedOut(UserDocument user, DateTime nowUtc) =>
        user.LockoutEndAt is not null &&
        EnsureUtc(user.LockoutEndAt.Value) > nowUtc;

    private async Task RecordFailedLoginAsync(
        UserDocument user,
        string ip,
        string userAgent,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var windowStart = user.FailedLoginWindowStartedAt is null
            ? nowUtc
            : EnsureUtc(user.FailedLoginWindowStartedAt.Value);
        var failedLoginCount = nowUtc - windowStart > TimeSpan.FromMinutes(_lockoutSettings.FailureWindowMinutes)
            ? 1
            : user.FailedLoginCount + 1;
        if (failedLoginCount == 1)
        {
            windowStart = nowUtc;
        }

        DateTime? lockoutEndAt = null;
        if (failedLoginCount >= _lockoutSettings.MaxFailedAttempts)
        {
            var overThresholdAttempts = Math.Min(
                failedLoginCount - _lockoutSettings.MaxFailedAttempts,
                10);
            var lockoutMinutes = Math.Min(
                _lockoutSettings.MaxLockoutMinutes,
                _lockoutSettings.BaseLockoutMinutes * Math.Pow(2, overThresholdAttempts));
            lockoutEndAt = nowUtc.AddMinutes(lockoutMinutes);
        }

        var update = Builders<UserDocument>.Update
            .Set(existingUser => existingUser.FailedLoginCount, failedLoginCount)
            .Set(existingUser => existingUser.FailedLoginWindowStartedAt, windowStart)
            .Set(existingUser => existingUser.LastFailedLoginAt, nowUtc)
            .Set(existingUser => existingUser.LockoutEndAt, lockoutEndAt)
            .Set(existingUser => existingUser.UpdatedAt, nowUtc);

        await _usersCollection.UpdateOneAsync(
            existingUser => existingUser.Id == user.Id,
            update,
            cancellationToken: cancellationToken);

        if (lockoutEndAt is null)
        {
            return;
        }

        _logger.LogWarning(
            "Temporarily locked account '{Username}' after {FailedLoginCount} failed login attempts.",
            user.Username,
            failedLoginCount);

        await _auditLogsCollection.InsertOneAsync(
            MutationDocumentFactory.CreateAuditLog(
                "LOGIN",
                user.Id,
                user.Id,
                "User",
                $"User {user.Username} was temporarily locked after repeated failed login attempts.",
                ip,
                userAgent,
                "LOCKED_OUT"),
            cancellationToken: cancellationToken);
    }

    private Task ResetFailedLoginsAsync(
        IClientSessionHandle session,
        ObjectId userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var resetUpdate = Builders<UserDocument>.Update
            .Set(existingUser => existingUser.FailedLoginCount, 0)
            .Unset("failedLoginWindowStartedAt")
            .Unset("lastFailedLoginAt")
            .Unset("lockoutEndAt")
            .Set(existingUser => existingUser.UpdatedAt, nowUtc);

        return _usersCollection.UpdateOneAsync(
            session,
            existingUser => existingUser.Id == userId,
            resetUpdate,
            cancellationToken: cancellationToken);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
