using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly DBContext _context;
    private readonly LogDbContext _logContext;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(DBContext context, LogDbContext logContext, ILogger<DashboardController> logger)
    {
        _context = context;
        _logContext = logContext;
        _logger = logger;
    }

    [HttpGet("stats")]
    [ProducesResponseType(typeof(DashboardStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardStats()
    {
        try
        {
            // Count backup plans currently in execution (endDateTime is null)
            var activeExecutions = await _logContext.BackupExecutions
                .CountAsync(e => e.endDateTime == null);

            // Get total files copied and total size across all executions
            var copyLogs = await _logContext.LogEntries
                .Where(log => log.action == "Copy")
                .ToListAsync();

            var totalFilesCopied = copyLogs.Count;
            var totalSizeCopied = copyLogs.Sum(log => log.size ?? 0);

            // Count total agents
            var totalAgents = await _context.Agents.CountAsync();

            // Count total backup plans
            var totalBackupPlans = await _context.BackupPlans.CountAsync();

            var stats = new DashboardStatsResponse
            {
                ActiveExecutions = activeExecutions,
                TotalFilesCopied = totalFilesCopied,
                TotalSizeCopied = totalSizeCopied,
                TotalAgents = totalAgents,
                TotalBackupPlans = totalBackupPlans
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard statistics");
            return StatusCode(500, new { message = "An error occurred while retrieving dashboard statistics", error = ex.Message });
        }
    }
}

public class DashboardStatsResponse
{
    public int ActiveExecutions { get; set; }
    public int TotalFilesCopied { get; set; }
    public long TotalSizeCopied { get; set; }
    public int TotalAgents { get; set; }
    public int TotalBackupPlans { get; set; }
}

