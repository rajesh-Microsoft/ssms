using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Dtos;
using SMMS.Api.Models;
using SMMS.Api.Services;

namespace SMMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(SmmsDbContext db, TokenService tokenService, AuditService audit) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (user is null)
            return Unauthorized(new { message = "Invalid username or password." });

        var status = user.Status.ToLowerInvariant();
        if (status == "pending")
            return Unauthorized(new { message = "Your account is awaiting Admin approval." });
        if (status != "active")
            return Unauthorized(new { message = "This account is inactive. Contact your admin." });

        var verify = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password." });

        var token = tokenService.CreateToken(user);
        return Ok(new LoginResponse(token, user.Id, user.Username, user.Role));
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        var exists = await db.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (exists)
            return Conflict(new { message = "That username is already taken." });

        var user = new User
        {
            Username = request.Username,
            Role = "Member",
            Status = "Pending",
            Email = request.Email,
            Mobile = request.Mobile,
            Flat = request.Flat,
            Floor = request.Floor,
            SecurityQuestion = request.SecurityQuestion
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        user.SecurityAnswerHash = Hasher.HashPassword(user, request.SecurityAnswer.Trim().ToLowerInvariant());

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Signup", $"New member sign-up request: {user.Username} ({request.Name}, Flat {request.Flat})");

        return Created(string.Empty, new { message = "Account request submitted! An Admin will review and activate it." });
    }

    [HttpGet("security-question")]
    [AllowAnonymous]
    public async Task<ActionResult<SecurityQuestionResponse>> GetSecurityQuestion([FromQuery] string username)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user is null || string.IsNullOrEmpty(user.SecurityQuestion))
            return NotFound(new { message = "No recovery option is set up for this account. Please contact your Admin." });

        return Ok(new SecurityQuestionResponse(user.SecurityQuestion));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
        if (user is null || string.IsNullOrEmpty(user.SecurityAnswerHash))
            return NotFound(new { message = "No recovery option is set up for this account. Please contact your Admin." });

        var verify = Hasher.VerifyHashedPassword(user, user.SecurityAnswerHash, request.SecurityAnswer.Trim().ToLowerInvariant());
        if (verify == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Incorrect answer." });

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync();
        await audit.LogAsync("Users", "Password Reset", $"Password self-reset via security question for user: {user.Username}");

        return Ok(new { message = "Password reset! You can now log in." });
    }
}
