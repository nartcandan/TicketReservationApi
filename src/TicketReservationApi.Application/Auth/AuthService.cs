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
    TimeProvider timeProvider,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator) : IAuthService
{
    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        // Fast path; a concurrent duplicate still hits the unique index (-> 409)
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new ConflictException("EmailTaken", "Email is already registered.");
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
        await db.SaveChangesAsync(cancellationToken);

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
