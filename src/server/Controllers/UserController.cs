using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;
using server.Services;

namespace server.Controllers;

[ApiController]
[Authorize]
public class UserController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<UserController> _logger;

    public UserController(DBContext context, ILogger<UserController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }
        return await _context.Users.FindAsync(userId);
    }

    private async Task<bool> IsCurrentUserAdminAsync()
    {
        var currentUser = await GetCurrentUserAsync();
        return currentUser?.isAdmin ?? false;
    }

    [HttpGet("/api/users")]
    [ProducesResponseType(typeof(List<UserResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers()
    {
        try
        {
            // Only admins can list all users
            if (!await IsCurrentUserAdminAsync())
            {
                return Forbid();
            }

            var users = await _context.Users
                .OrderBy(u => u.username)
                .Select(u => new UserResponse
                {
                    Id = u.id,
                    Username = u.username,
                    Email = u.email,
                    IsAdmin = u.isAdmin,
                    IsActive = u.isActive,
                    CreatedAt = u.createdAt,
                    UpdatedAt = u.updatedAt,
                    Timezone = u.timezone,
                    Theme = u.theme
                })
                .ToListAsync();

            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users");
            return StatusCode(500, new { message = "An error occurred while retrieving users", error = ex.Message });
        }
    }

    [HttpGet("/api/users/{id}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(Guid id)
    {
        try
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var response = new UserResponse
            {
                Id = user.id,
                Username = user.username,
                Email = user.email,
                IsAdmin = user.isAdmin,
                IsActive = user.isActive,
                CreatedAt = user.createdAt,
                UpdatedAt = user.updatedAt,
                Timezone = user.timezone,
                Theme = user.theme
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving user", error = ex.Message });
        }
    }

    [HttpPost("/api/users")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        // Only admins can create users
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        try
        {
            // Validate username uniqueness
            if (await _context.Users.AnyAsync(u => u.username == request.Username))
            {
                return BadRequest(new { message = "Username already exists" });
            }

            // Validate email uniqueness
            if (await _context.Users.AnyAsync(u => u.email == request.Email))
            {
                return BadRequest(new { message = "Email already exists" });
            }

            // Validate password
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 3)
            {
                return BadRequest(new { message = "Password must be at least 3 characters long" });
            }

            var user = new User
            {
                id = Guid.NewGuid(),
                username = request.Username,
                email = request.Email,
                passwordHash = AuthService.HashPassword(request.Password),
                isAdmin = request.IsAdmin ?? false,
                isActive = true,
                createdAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var response = new UserResponse
            {
                Id = user.id,
                Username = user.username,
                Email = user.email,
                IsAdmin = user.isAdmin,
                IsActive = user.isActive,
                CreatedAt = user.createdAt,
                UpdatedAt = user.updatedAt,
                Timezone = user.timezone,
                Theme = user.theme
            };

            return CreatedAtAction(nameof(GetUser), new { id = user.id }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, new { message = "An error occurred while creating user", error = ex.Message });
        }
    }

    [HttpPut("/api/users/{id}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        // Only admins can update users
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        try
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Validate username uniqueness (if changed)
            if (request.Username != null && request.Username != user.username)
            {
                if (await _context.Users.AnyAsync(u => u.username == request.Username && u.id != id))
                {
                    return BadRequest(new { message = "Username already exists" });
                }
                user.username = request.Username;
            }

            // Validate email uniqueness (if changed)
            if (request.Email != null && request.Email != user.email)
            {
                if (await _context.Users.AnyAsync(u => u.email == request.Email && u.id != id))
                {
                    return BadRequest(new { message = "Email already exists" });
                }
                user.email = request.Email;
            }

            // Update password if provided
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                if (request.Password.Length < 3)
                {
                    return BadRequest(new { message = "Password must be at least 3 characters long" });
                }
                user.passwordHash = AuthService.HashPassword(request.Password);
            }

            // Update admin status if provided
            if (request.IsAdmin.HasValue)
            {
                user.isAdmin = request.IsAdmin.Value;
            }

            // Update active status if provided
            if (request.IsActive.HasValue)
            {
                user.isActive = request.IsActive.Value;
            }

            // Update timezone if provided
            if (request.Timezone != null)
            {
                user.timezone = request.Timezone;
            }

            // Update theme if provided
            if (request.Theme != null)
            {
                user.theme = request.Theme;
            }

            user.updatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var response = new UserResponse
            {
                Id = user.id,
                Username = user.username,
                Email = user.email,
                IsAdmin = user.isAdmin,
                IsActive = user.isActive,
                CreatedAt = user.createdAt,
                UpdatedAt = user.updatedAt,
                Timezone = user.timezone,
                Theme = user.theme
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while updating user", error = ex.Message });
        }
    }

    [HttpPut("/api/users/me/change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            // Get current user from JWT claims
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Validate new password
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 3)
            {
                return BadRequest(new { message = "New password must be at least 3 characters long" });
            }

            // Update password
            user.passwordHash = AuthService.HashPassword(request.NewPassword);
            user.updatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, new { message = "An error occurred while changing password", error = ex.Message });
        }
    }

    [HttpPut("/api/users/me/change-username")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeUsername([FromBody] ChangeUsernameRequest request)
    {
        try
        {
            // Only admins can change their own username
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            if (!currentUser.isAdmin)
            {
                return Forbid();
            }

            // Validate new username
            if (string.IsNullOrWhiteSpace(request.NewUsername) || request.NewUsername.Length < 3)
            {
                return BadRequest(new { message = "Username must be at least 3 characters long" });
            }

            // Check if username is already taken
            if (await _context.Users.AnyAsync(u => u.username == request.NewUsername && u.id != currentUser.id))
            {
                return BadRequest(new { message = "Username already exists" });
            }

            // Prevent changing default admin username
            if (currentUser.username == "admin" && request.NewUsername != "admin")
            {
                return BadRequest(new { message = "Cannot change the default admin username" });
            }

            // Update username
            currentUser.username = request.NewUsername;
            currentUser.updatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var response = new UserResponse
            {
                Id = currentUser.id,
                Username = currentUser.username,
                Email = currentUser.email,
                IsAdmin = currentUser.isAdmin,
                IsActive = currentUser.isActive,
                CreatedAt = currentUser.createdAt,
                UpdatedAt = currentUser.updatedAt,
                Timezone = currentUser.timezone,
                Theme = currentUser.theme
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing username");
            return StatusCode(500, new { message = "An error occurred while changing username", error = ex.Message });
        }
    }

    [HttpPut("/api/users/me/preferences")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyPreferences([FromBody] UpdatePreferencesRequest request)
    {
        try
        {
            // Get current user from JWT claims
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            _logger.LogInformation("Updating preferences for user {UserId}. Current timezone: {CurrentTimezone}, New timezone: {NewTimezone}",
                userId, user.timezone, request.Timezone);

            // Update timezone if provided
            if (request.Timezone != null)
            {
                user.timezone = request.Timezone;
                _logger.LogInformation("Timezone updated to: {Timezone}", user.timezone);
            }

            // Update theme if provided
            if (request.Theme != null)
            {
                user.theme = request.Theme;
                _logger.LogInformation("Theme updated to: {Theme}", user.theme);
            }

            user.updatedAt = DateTime.UtcNow;

            // Explicitly mark the entity as modified to ensure EF Core tracks the changes
            _context.Entry(user).State = EntityState.Modified;

            var changesSaved = await _context.SaveChangesAsync();
            _logger.LogInformation("SaveChanges completed. Changes saved: {ChangesSaved}. User timezone is now: {Timezone}",
                changesSaved, user.timezone);

            var response = new UserResponse
            {
                Id = user.id,
                Username = user.username,
                Email = user.email,
                IsAdmin = user.isAdmin,
                IsActive = user.isActive,
                CreatedAt = user.createdAt,
                UpdatedAt = user.updatedAt,
                Timezone = user.timezone,
                Theme = user.theme
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user preferences");
            return StatusCode(500, new { message = "An error occurred while updating preferences", error = ex.Message });
        }
    }

    [HttpGet("/api/users/me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser()
    {
        try
        {
            // Get current user from JWT claims
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var response = new UserResponse
            {
                Id = user.id,
                Username = user.username,
                Email = user.email,
                IsAdmin = user.isAdmin,
                IsActive = user.isActive,
                CreatedAt = user.createdAt,
                UpdatedAt = user.updatedAt,
                Timezone = user.timezone,
                Theme = user.theme
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current user");
            return StatusCode(500, new { message = "An error occurred while retrieving user", error = ex.Message });
        }
    }

    [HttpPut("/api/users/{id}/change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeUserPassword(Guid id, [FromBody] ChangePasswordRequest request)
    {
        // Only admins can change other users' passwords
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        try
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Validate new password
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 3)
            {
                return BadRequest(new { message = "New password must be at least 3 characters long" });
            }

            // Update password
            user.passwordHash = AuthService.HashPassword(request.NewPassword);
            user.updatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while changing password", error = ex.Message });
        }
    }

    [HttpPut("/api/users/{id}/change-username")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeUserUsername(Guid id, [FromBody] ChangeUsernameRequest request)
    {
        // Only admins can change other users' usernames
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        try
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Validate new username
            if (string.IsNullOrWhiteSpace(request.NewUsername) || request.NewUsername.Length < 3)
            {
                return BadRequest(new { message = "Username must be at least 3 characters long" });
            }

            // Check if username is already taken
            if (await _context.Users.AnyAsync(u => u.username == request.NewUsername && u.id != id))
            {
                return BadRequest(new { message = "Username already exists" });
            }

            // Prevent changing default admin username
            if (user.username == "admin" && request.NewUsername != "admin")
            {
                return BadRequest(new { message = "Cannot change the default admin username" });
            }

            // Update username
            user.username = request.NewUsername;
            user.updatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var response = new UserResponse
            {
                Id = user.id,
                Username = user.username,
                Email = user.email,
                IsAdmin = user.isAdmin,
                IsActive = user.isActive,
                CreatedAt = user.createdAt,
                UpdatedAt = user.updatedAt,
                Timezone = user.timezone,
                Theme = user.theme
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing username for user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while changing username", error = ex.Message });
        }
    }

    [HttpDelete("/api/users/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        // Only admins can delete users
        if (!await IsCurrentUserAdminAsync())
        {
            return Forbid();
        }

        try
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Prevent deleting the default admin user
            if (user.username == "admin")
            {
                return BadRequest(new { message = "Cannot delete the default admin user" });
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting user", error = ex.Message });
        }
    }
}

public class UserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Timezone { get; set; }
    public string? Theme { get; set; }
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool? IsAdmin { get; set; }
}

public class UpdateUserRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool? IsAdmin { get; set; }
    public bool? IsActive { get; set; }
    public string? Timezone { get; set; }
    public string? Theme { get; set; }
}

public class UpdatePreferencesRequest
{
    public string? Timezone { get; set; }
    public string? Theme { get; set; }
}

public class ChangePasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangeUsernameRequest
{
    public string NewUsername { get; set; } = string.Empty;
}

