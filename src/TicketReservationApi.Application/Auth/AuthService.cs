using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Auth;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AccessToken> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public class AuthService(
    IAppDbContext db,
    IPasswordHasher<User> passwordHasher,
    IAccessTokenGenerator tokenGenerator,
    IDatabaseErrors databaseErrors,
    TimeProvider timeProvider,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator) : IAuthService
{
    private static ConflictException EmailTaken() => new("EmailTaken", "Email is already registered.");

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        // Early check for a clear error; a concurrent duplicate is caught by the unique index below
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw EmailTaken();
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            FullName = request.FullName.Trim(),
            // Registration never grants Admin; admins are seeded from configuration
            Role = UserRole.User,
            CreatedAt = timeProvider.GetUtcNow()
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (databaseErrors.IsUniqueViolation(ex))
        {
            // The only unique key on a new user besides the generated Id is Email
            throw EmailTaken();
        }

        return new UserResponse(user.Id, user.Email, user.FullName, user.Role.ToString());
    }

    public async Task<AccessToken> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Same error for unknown email and wrong password, so accounts cannot be enumerated
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }

        return tokenGenerator.Generate(user);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
