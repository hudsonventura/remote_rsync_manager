using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(DBContext context, ILogger<NotificationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("/api/notifications")]
    [ProducesResponseType(typeof(List<NotificationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool? unreadOnly = false,
        [FromQuery] int limit = 50)
    {
        try
        {
            var query = _context.Notifications.AsQueryable();

            if (unreadOnly == true)
            {
                query = query.Where(n => !n.isRead);
            }

            var notifications = await query
                .OrderByDescending(n => n.createdAt)
                .Take(limit)
                .Select(n => new NotificationResponse
                {
                    Id = n.id,
                    Type = n.type,
                    Title = n.title,
                    Message = n.message,
                    BackupPlanId = n.backupPlanId,
                    ExecutionId = n.executionId,
                    IsRead = n.isRead,
                    CreatedAt = n.createdAt
                })
                .ToListAsync();

            return Ok(notifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notifications");
            return StatusCode(500, new { message = "An error occurred while retrieving notifications", error = ex.Message });
        }
    }

    [HttpGet("/api/notifications/unread-count")]
    [ProducesResponseType(typeof(UnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        try
        {
            var count = await _context.Notifications
                .Where(n => !n.isRead)
                .CountAsync();

            return Ok(new UnreadCountResponse { Count = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unread notification count");
            return StatusCode(500, new { message = "An error occurred while retrieving unread count", error = ex.Message });
        }
    }

    [HttpPost("/api/notifications/{id}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        try
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null)
            {
                return NotFound(new { message = "Notification not found" });
            }

            notification.isRead = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Notification marked as read" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as read");
            return StatusCode(500, new { message = "An error occurred while marking notification as read", error = ex.Message });
        }
    }

    [HttpPost("/api/notifications/mark-all-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        try
        {
            var unreadNotifications = await _context.Notifications
                .Where(n => !n.isRead)
                .ToListAsync();

            foreach (var notification in unreadNotifications)
            {
                notification.isRead = true;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "All notifications marked as read", count = unreadNotifications.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read");
            return StatusCode(500, new { message = "An error occurred while marking all notifications as read", error = ex.Message });
        }
    }

    [HttpDelete("/api/notifications/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteNotification(Guid id)
    {
        try
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null)
            {
                return NotFound(new { message = "Notification not found" });
            }

            _context.Notifications.Remove(notification);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Notification deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification");
            return StatusCode(500, new { message = "An error occurred while deleting notification", error = ex.Message });
        }
    }
}

public class NotificationResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? BackupPlanId { get; set; }
    public Guid? ExecutionId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UnreadCountResponse
{
    public int Count { get; set; }
}

